# Sprint 1 of 3: Bulk status endpoint — contract, validation, happy path
Spec: .harness/output/spec.md — business rules covered: BR-1, BR-2, BR-3, BR-4, BR-5, BR-6

## Goal
Add `PATCH /api/activities/bulk-status` that validates the request and sets every listed same-store
activity to `DONE` or `BLOCKED`, returning a per-item result and publishing one `ActivityStatusChanged`
per changed activity.

## Background (facts from the current code)
- `ActivityService` already owns `UpdateAsync`, store scoping (`GetVisibleAsync` → `NotFoundError`), and
  publishes `ActivityStatusChanged(ActivityId, StoreId, Title, PreviousStatus, NewStatus, Priority,
  AssigneeId, ChangedBy, OccurredAt)` from `Activities.Contracts`. Reuse these; do not add a new event.
- Alerts' existing `ActivityBlockedSubscriber` creates an `ESCALATION` / `IN_APP` notification for every
  active store manager of the store when `NewStatus` is `BLOCKED`. Do not change Alerts.
- Enum wire values come from `StoreOpsJson` (`"DONE"`, `"BLOCKED"`; a non-wire string such as `"done"`
  is rejected by the shared converter with 400 `VALIDATION_FAILED`).
- `PATCH /api/activities/{id}` already exists; the literal route `/bulk-status` must win over `{id}`
  (ASP.NET Core prefers literal segments) — AC-1.1 proves the bulk handler is the one reached.

## Request / response
```
PATCH /api/activities/bulk-status
{ "status": "DONE" | "BLOCKED", "activityIds": ["act-…", …] }

200 {
  "status": "DONE",
  "requested": 2, "updated": 2, "unchanged": 0, "failed": 0,
  "results": [
    { "activityId": "act-…", "outcome": "UPDATED", "previousStatus": "TODO", "newStatus": "DONE", "error": null },
    …
  ]
}
```
- `results` has one entry per requested id, in request order.
- `outcome` wire values: `UPDATED`, `UNCHANGED`, `FAILED` (enum `BulkStatusOutcome`).
- For `UNCHANGED`: `previousStatus` = `newStatus` = current status. For `FAILED` (sprint 2):
  `error = { "code", "message" }`, statuses null.
- No AC in this sprint sends a missing or other-store id; sprint 2 defines per-item failures. Process ids
  in a loop, one at a time, reusing the existing store-scoped lookup, so sprint 2 can add the per-item
  `AppError` catch without restructuring.

## Files expected to change
| Path | Layer |
|---|---|
| `src/StoreOps.Api/Modules/Activities/Activity.cs` (or a new `BulkStatusUpdate.cs` in the same folder) — `BulkStatusUpdateRequest`, `BulkStatusUpdateResponse`, `BulkStatusItemResult`, `BulkStatusItemError`, `BulkStatusOutcome` (all `internal`) | Record |
| `src/StoreOps.Api/Modules/Activities/ActivityService.cs` — `BulkUpdateStatusAsync(CurrentUser user, BulkStatusUpdateRequest request, CancellationToken ct)` | Service |
| `src/StoreOps.Api/Modules/Activities/ActivityRoutes.cs` — `group.MapPatch("/bulk-status", …).WithName("BulkUpdateActivityStatus")`, returns `TypedResults.Ok(...)` | Routes |
| `tests/StoreOps.Api.Tests/Modules/Activities/ActivityBulkStatusServiceTests.cs` | Test |
| `tests/StoreOps.Api.Tests/Modules/Activities/ActivityBulkStatusRoutesTests.cs` | Test |

## Acceptance criteria
In every AC, "act-A", "act-B", "act-C" are activities the test first creates with
`POST /api/activities` as `dev-token-store-manager` (store-001); they start as `TODO`.

### AC-1.1 Several activities marked DONE in one request  (BR-4, BR-6)
GIVEN act-A and act-B exist in store-001 with status TODO
WHEN  `dev-token-store-manager` sends `PATCH /api/activities/bulk-status`
      `{ "status": "DONE", "activityIds": ["act-A", "act-B"] }`
THEN  200; body `status` = `DONE`, `requested` = 2, `updated` = 2, `unchanged` = 0, `failed` = 0;
      `results[0].activityId` = act-A and `results[1].activityId` = act-B, both `outcome` = `UPDATED`,
      `previousStatus` = `TODO`, `newStatus` = `DONE`;
      `GET /api/activities/act-A` and `/act-B` both return `status` = `DONE` with `updatedAt` later than or
      equal to `createdAt`;
      exactly two `ActivityStatusChanged` events are published (act-A, act-B), each with
      `PreviousStatus` = Todo, `NewStatus` = Done, `ChangedBy` = `usr-sm-001`, `StoreId` = store-001.

### AC-1.2 BLOCKED via bulk update triggers the existing escalation  (BR-6)
GIVEN act-A exists in store-001 with status TODO and `GET /api/alerts` as `dev-token-store-manager`
      returns no notification with `sourceActivityId` = act-A
WHEN  `dev-token-department-lead` sends `PATCH /api/activities/bulk-status`
      `{ "status": "BLOCKED", "activityIds": ["act-A"] }`
THEN  200; `results[0].outcome` = `UPDATED`, `newStatus` = `BLOCKED`; act-A is `BLOCKED`;
      exactly one `ActivityStatusChanged` event with `NewStatus` = Blocked and `ChangedBy` = `usr-dl-001`;
      `GET /api/alerts` as `dev-token-store-manager` now returns exactly one notification with
      `type` = `ESCALATION`, `channel` = `IN_APP`, `sourceActivityId` = act-A.

### AC-1.3 Target status other than DONE/BLOCKED is rejected  (BR-2)
GIVEN act-A exists in store-001 with status TODO
WHEN  `dev-token-store-manager` sends `PATCH /api/activities/bulk-status` with, in turn:
      (a) `{ "status": "IN_PROGRESS", "activityIds": ["act-A"] }`
      (b) `{ "status": "TODO", "activityIds": ["act-A"] }`
      (c) `{ "activityIds": ["act-A"] }` (status missing)
      (d) `{ "status": "done", "activityIds": ["act-A"] }` (not a wire value)
THEN  each returns 400 with `error.code` = `VALIDATION_FAILED`; for (a), (b), (c) `error.field` = `status`;
      act-A is still `TODO` with unchanged `updatedAt`; no `ActivityStatusChanged` event is published.

### AC-1.4 Invalid activityIds list is rejected as a whole  (BR-3)
GIVEN act-A exists in store-001 with status TODO
WHEN  `dev-token-store-manager` sends `PATCH /api/activities/bulk-status` with `status` = `DONE` and, in turn:
      (a) `activityIds` missing, (b) `[]`, (c) `["act-A", "  "]`, (d) `["act-A", "act-A"]`,
      (e) 51 ids where the first is act-A and the rest are any distinct non-blank strings
THEN  each returns 400 with `error.code` = `VALIDATION_FAILED` and `error.field` = `activityIds`;
      act-A is still `TODO` (it was not updated even though it is valid and listed first);
      no `ActivityStatusChanged` event is published.
      AND a request with exactly 50 distinct ids of existing store-001 activities returns 200 with `updated` = 50.

### AC-1.5 Activity already in the target status is UNCHANGED  (BR-4, BR-5)
GIVEN act-A is `BLOCKED` (set beforehand with `PATCH /api/activities/act-A { "status": "BLOCKED" }`) and
      act-B is `TODO`; the test records act-A's `updatedAt` and clears captured events after the setup
WHEN  `dev-token-store-manager` sends `PATCH /api/activities/bulk-status`
      `{ "status": "BLOCKED", "activityIds": ["act-A", "act-B"] }`
THEN  200; `requested` = 2, `updated` = 1, `unchanged` = 1, `failed` = 0;
      `results[0]` = { act-A, `UNCHANGED`, `previousStatus` = `BLOCKED`, `newStatus` = `BLOCKED` };
      `results[1]` = { act-B, `UPDATED`, `TODO` → `BLOCKED` };
      act-A's `updatedAt` is unchanged; exactly one `ActivityStatusChanged` event, for act-B.

### AC-1.6 Request without a bearer token is rejected  (BR-1)
GIVEN act-A exists in store-001 with status TODO
WHEN  a client with **no** Authorization header (factory configured to require tokens, as in the existing
      auth tests) sends `PATCH /api/activities/bulk-status` `{ "status": "DONE", "activityIds": ["act-A"] }`
THEN  401 with `error.code` = `UNAUTHORIZED`; act-A is still `TODO`; no `ActivityStatusChanged` event.

## Out of scope for this sprint
- Per-item failure outcomes (`FAILED` with NOT_FOUND / FORBIDDEN / BUSINESS_RULE_VIOLATION), associate
  permission rule, partial-failure guarantees — sprint 2.
- Audit entries, `note` field, `GET /api/activities/{id}/audit` — sprint 3.
- Any change to Alerts, Staff, Programmes, Reports, or the existing single-activity endpoints.

## Definition of done
- Every AC has at least one named test that proves it (test name contains `AC1_1` … `AC1_6`), asserting the
  resulting activity state and the published events — not only the status code.
- No new `throw` other than the `AppError` hierarchy; routes contain no business `if`.
- `pwsh eng/verify.ps1` prints `VERIFY: PASS`.
