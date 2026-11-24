# Sprint 2 of 3: Per-item failure handling — store scope, permissions, reopen rule
Spec: .harness/output/spec.md — business rules covered: BR-7, BR-8, BR-9, BR-10

## Goal
Make `PATCH /api/activities/bulk-status` process every id independently, so a missing, other-store,
forbidden or not-reopenable activity is reported as a `FAILED` item while every other item is still
processed and the request returns 200.

## Background (facts from the current code and sprint 1)
- Sprint 1 delivered `BulkStatusUpdateRequest`, `BulkStatusUpdateResponse`, `BulkStatusItemResult`,
  `BulkStatusItemError { Code, Message }`, `BulkStatusOutcome` (`UPDATED UNCHANGED FAILED`) and
  `ActivityService.BulkUpdateStatusAsync`, plus the route `PATCH /api/activities/bulk-status`.
- Request-level validation (BR-2, BR-3) still rejects the whole request with 400 before any item runs.
- `CurrentUser` carries `UserId`, `StoreId`, `Role` (`StaffRole`); no cross-module read is needed.
- Per-item failures are expressed by throwing the existing `AppError` subclasses (`NotFoundError`,
  `ForbiddenError`, `BusinessRuleError`) inside the per-item step and catching `AppError` in the service
  loop to produce `outcome = FAILED`, `error.code = <AppError code>`, `error.message = <AppError message>`.
  Never catch `Exception` broadly; never `throw new Exception`.
- Per-item check order (BR-7), first match wins:
  1. missing, or `StoreId != caller.StoreId` → `NOT_FOUND`
  2. caller is `ASSOCIATE` and activity's `AssigneeId != caller.UserId` and `OwnerId != caller.UserId` → `FORBIDDEN`
  3. activity already has the target status → `UNCHANGED` (sprint 1 behaviour)
  4. activity is `DONE` and target is `BLOCKED` → `BUSINESS_RULE_VIOLATION`
  5. otherwise → save, publish `ActivityStatusChanged`, `UPDATED`
- `FAILED` results: `previousStatus` and `newStatus` are `null`; `error` is set. `UPDATED`/`UNCHANGED`
  results: `error` is `null`.

## Files expected to change
| Path | Layer |
|---|---|
| `src/StoreOps.Api/Modules/Activities/ActivityService.cs` — per-item loop with `AppError` catch, associate rule, reopen rule | Service |
| `src/StoreOps.Api/Modules/Activities/Activity.cs` (or `BulkStatusUpdate.cs`) — only if the result records need adjusting | Record |
| `tests/StoreOps.Api.Tests/Modules/Activities/ActivityBulkStatusServiceTests.cs` | Test |
| `tests/StoreOps.Api.Tests/Modules/Activities/ActivityBulkStatusRoutesTests.cs` | Test |

## Acceptance criteria
Unless stated otherwise, activities are created by the test with `POST /api/activities` and start `TODO`.
Captured events are cleared after setup so each THEN counts only events from the bulk request.

### AC-2.1 Missing and other-store activities fail as NOT_FOUND, others still update  (BR-8, BR-7)
GIVEN act-A exists in store-001 (created by `dev-token-store-manager`) and act-B exists in store-002
      (created by `dev-token-store-002-manager`), both `TODO`; `act-missing` does not exist
WHEN  `dev-token-store-manager` sends `PATCH /api/activities/bulk-status`
      `{ "status": "DONE", "activityIds": ["act-B", "act-missing", "act-A"] }`
THEN  200; `requested` = 3, `updated` = 1, `unchanged` = 0, `failed` = 2;
      `results[0]` = { act-B, `FAILED`, `error.code` = `NOT_FOUND` };
      `results[1]` = { act-missing, `FAILED`, `error.code` = `NOT_FOUND` };
      `results[2]` = { act-A, `UPDATED`, `TODO` → `DONE` };
      act-B is still `TODO` (checked with `GET /api/activities/act-B` as `dev-token-store-002-manager`)
      with unchanged `updatedAt`; act-A is `DONE`;
      exactly one `ActivityStatusChanged` event (for act-A).

### AC-2.2 Associate may only update activities assigned to or owned by them  (BR-9)
GIVEN act-A (store-001) created by `dev-token-store-manager` with `assigneeId` = `usr-as-001`;
      act-B (store-001) created by `dev-token-store-manager` with no assignee;
      act-C (store-001) created by `dev-token-associate` (owner `usr-as-001`), no assignee; all `TODO`
WHEN  `dev-token-associate` sends `PATCH /api/activities/bulk-status`
      `{ "status": "DONE", "activityIds": ["act-A", "act-B", "act-C"] }`
THEN  200; `updated` = 2, `failed` = 1;
      `results[0]` = { act-A, `UPDATED` }; `results[1]` = { act-B, `FAILED`, `error.code` = `FORBIDDEN` };
      `results[2]` = { act-C, `UPDATED` };
      act-B is still `TODO` with unchanged `updatedAt`; act-A and act-C are `DONE`;
      exactly two `ActivityStatusChanged` events (act-A, act-C), `ChangedBy` = `usr-as-001`.

### AC-2.3 Non-associate roles may update any activity in their own store  (BR-9)
GIVEN act-A and act-B (store-001) created by `dev-token-store-manager`, no assignee, both `TODO`
WHEN  `dev-token-department-lead` sends `{ "status": "BLOCKED", "activityIds": ["act-A"] }` and
      `dev-token-regional-manager` sends `{ "status": "DONE", "activityIds": ["act-B"] }`
      to `PATCH /api/activities/bulk-status`
THEN  both return 200 with `updated` = 1, `failed` = 0; act-A is `BLOCKED`, act-B is `DONE`;
      the two `ActivityStatusChanged` events have `ChangedBy` = `usr-dl-001` and `usr-rm-001` respectively.

### AC-2.4 A DONE activity cannot be set to BLOCKED; BLOCKED can be set to DONE  (BR-10, BR-7)
GIVEN act-A is `DONE` and act-B is `BLOCKED` (both set beforehand via `PATCH /api/activities/{id}` as
      `dev-token-store-manager`), act-C is `TODO`
WHEN  `dev-token-store-manager` sends `PATCH /api/activities/bulk-status`
      `{ "status": "BLOCKED", "activityIds": ["act-A", "act-C"] }`
      and then `{ "status": "DONE", "activityIds": ["act-B"] }`
THEN  first response 200: `results[0]` = { act-A, `FAILED`, `error.code` = `BUSINESS_RULE_VIOLATION` },
      `results[1]` = { act-C, `UPDATED`, `TODO` → `BLOCKED` }; act-A is still `DONE` with unchanged
      `updatedAt`; exactly one `ActivityStatusChanged` (act-C) for that request; no `ESCALATION`
      notification with `sourceActivityId` = act-A is created;
      second response 200: `results[0]` = { act-B, `UPDATED`, `BLOCKED` → `DONE` }.

### AC-2.5 Every item failing still returns 200 and changes nothing  (BR-7, BR-8)
GIVEN act-B exists in store-002 (`TODO`); `act-missing` does not exist
WHEN  `dev-token-store-manager` sends `PATCH /api/activities/bulk-status`
      `{ "status": "DONE", "activityIds": ["act-missing", "act-B"] }`
THEN  200; `requested` = 2, `updated` = 0, `unchanged` = 0, `failed` = 2; both results `FAILED` with
      `NOT_FOUND`, each with a non-empty `error.message`; act-B is still `TODO`; no `ActivityStatusChanged` event.

### AC-2.6 Check order: NOT_FOUND beats FORBIDDEN, FORBIDDEN beats UNCHANGED  (BR-7)
GIVEN act-A (store-001) created by `dev-token-store-manager`, no assignee, already `DONE`;
      act-B exists in store-002
WHEN  `dev-token-associate` sends `PATCH /api/activities/bulk-status`
      `{ "status": "DONE", "activityIds": ["act-A", "act-B"] }`
THEN  200; `results[0]` = { act-A, `FAILED`, `error.code` = `FORBIDDEN` } (not `UNCHANGED`);
      `results[1]` = { act-B, `FAILED`, `error.code` = `NOT_FOUND` } (not `FORBIDDEN`);
      `unchanged` = 0, `failed` = 2; no `ActivityStatusChanged` event; both activities keep their status
      and `updatedAt`.

## Out of scope for this sprint
- Audit entries, `note`, `GET /api/activities/{id}/audit` — sprint 3.
- Changing the existing single `PATCH /api/activities/{id}` permission rules.
- Department-based restrictions for `DEPARTMENT_LEAD`.
- All-or-nothing / transactional mode.

## Definition of done
- Every AC has at least one named test that proves it (test name contains `AC2_1` … `AC2_6`), asserting
  per-item outcomes, the unchanged state of failed activities, and the exact events published.
- All sprint-1 tests still pass.
- `pwsh eng/verify.ps1` prints `VERIFY: PASS`.
