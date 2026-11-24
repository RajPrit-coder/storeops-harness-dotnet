# Sprint 3 of 3: Audit trail per updated activity + handover note
Spec: .harness/output/spec.md — business rules covered: BR-11, BR-12, BR-13

## Goal
Record one audit entry for every activity whose status a bulk update changed, carry an optional handover
`note` into those entries, and expose the trail through `GET /api/activities/{id}/audit`.

## Background (facts from the current code and sprints 1–2)
- `PATCH /api/activities/bulk-status` and `ActivityService.BulkUpdateStatusAsync` exist with per-item
  outcomes `UPDATED` / `UNCHANGED` / `FAILED` and publish `ActivityStatusChanged` per `UPDATED` item.
- The audit trail is **owned by the Activities module** (no new module, no new module dependency, no new
  event). It is written by `ActivityService` through a new Activities repository, right after the activity
  save succeeds and before/alongside the `ActivityStatusChanged` publish for that item.
- Follow the existing naming pattern exactly: `Activity` record + `IActivityRepository` /
  `InMemoryActivityRepository` registered as a singleton in `ActivitiesModule`.
  If `EveryModuleType_HasAKnownRole` rejects a new type name, rename the type to match the conventions —
  never touch `tests/StoreOps.Api.Tests/Architecture/`.
- Audit entry ids use `Ids.New("aud")`. `occurredAt` = the `updatedAt` written to the activity for that item
  (from the injected `TimeProvider`).

## Data shapes
```
internal sealed record ActivityAuditEntry(
    string Id, string ActivityId, string StoreId,
    ActivityStatus PreviousStatus, ActivityStatus NewStatus,
    string ChangedBy, string Source, string? Note, DateTimeOffset OccurredAt)   // ToDto()

GET /api/activities/{id}/audit → 200
[ { "id": "aud-…", "activityId": "act-…", "previousStatus": "TODO", "newStatus": "DONE",
    "changedBy": "usr-sm-001", "source": "BULK_STATUS_UPDATE", "note": "…" | null,
    "occurredAt": "…" } ]          // oldest first; [] when none
```
`BulkStatusUpdateRequest` gains `string? Note` (optional, max 500 characters).

## Files expected to change
| Path | Layer |
|---|---|
| `src/StoreOps.Api/Modules/Activities/ActivityAuditEntry.cs` — `ActivityAuditEntry`, `ActivityAuditEntryDto` (internal) | Record |
| `src/StoreOps.Api/Modules/Activities/ActivityAuditRepository.cs` — `IActivityAuditRepository` (`AppendAsync`, `ListByActivityAsync`), `InMemoryActivityAuditRepository` | Repository |
| `src/StoreOps.Api/Modules/Activities/ActivitiesModule.cs` — register the audit repository as singleton | Module (composition) |
| `src/StoreOps.Api/Modules/Activities/Activity.cs` (or `BulkStatusUpdate.cs`) — `Note` on `BulkStatusUpdateRequest` | Record |
| `src/StoreOps.Api/Modules/Activities/ActivityService.cs` — note validation, audit append per `UPDATED` item, `ListAuditAsync(CurrentUser, string id, CancellationToken)` | Service |
| `src/StoreOps.Api/Modules/Activities/ActivityRoutes.cs` — `group.MapGet("/{id}/audit", …).WithName("ListActivityAudit")` | Routes |
| `tests/StoreOps.Api.Tests/Modules/Activities/ActivityBulkStatusServiceTests.cs` | Test |
| `tests/StoreOps.Api.Tests/Modules/Activities/ActivityBulkStatusRoutesTests.cs` | Test |

## Acceptance criteria
Activities are created by the test with `POST /api/activities` as `dev-token-store-manager` (store-001)
unless stated otherwise, and start `TODO`.

### AC-3.1 Each updated activity gets exactly one audit entry  (BR-11)
GIVEN act-A and act-B exist in store-001, both `TODO`, and `GET /api/activities/act-A/audit` returns `[]`
WHEN  `dev-token-store-manager` sends `PATCH /api/activities/bulk-status`
      `{ "status": "DONE", "activityIds": ["act-A", "act-B"] }`
THEN  200 with `updated` = 2;
      `GET /api/activities/act-A/audit` returns exactly one entry with `activityId` = act-A,
      `previousStatus` = `TODO`, `newStatus` = `DONE`, `changedBy` = `usr-sm-001`,
      `source` = `BULK_STATUS_UPDATE`, `note` = null, `id` starting with `aud-`, and `occurredAt` equal to
      act-A's `updatedAt` from `GET /api/activities/act-A`;
      `GET /api/activities/act-B/audit` returns exactly one entry for act-B with the same field rules;
      the audit repository holds exactly 2 entries in total.

### AC-3.2 UNCHANGED and FAILED items get no audit entry  (BR-11)
GIVEN act-A is `DONE` (set via `PATCH /api/activities/act-A`), act-B is `BLOCKED` (set via
      `PATCH /api/activities/act-B`), act-C is `TODO`, act-X exists in store-002; no audit entries exist
WHEN  `dev-token-store-manager` sends `PATCH /api/activities/bulk-status`
      `{ "status": "BLOCKED", "activityIds": ["act-A", "act-B", "act-C", "act-X"] }`
THEN  200; outcomes: act-A `FAILED` (`BUSINESS_RULE_VIOLATION`), act-B `UNCHANGED`, act-C `UPDATED`,
      act-X `FAILED` (`NOT_FOUND`);
      `GET /api/activities/act-A/audit` and `GET /api/activities/act-B/audit` return `[]`;
      `GET /api/activities/act-C/audit` returns exactly one entry `TODO` → `BLOCKED`;
      the audit repository holds no entry for act-X and exactly 1 entry in total.

### AC-3.3 Handover note is stored on every updated activity's entry; an over-long note is rejected  (BR-13)
GIVEN act-A and act-B exist in store-001, both `TODO`
WHEN  `dev-token-department-lead` sends `PATCH /api/activities/bulk-status`
      `{ "status": "BLOCKED", "activityIds": ["act-A", "act-B"], "note": "Aisle 7 shelf damaged, waiting on maintenance" }`
      and then `dev-token-store-manager` sends
      `{ "status": "DONE", "activityIds": ["act-A"], "note": "<501 × 'x'>" }`
THEN  first response 200; the single audit entry of act-A and of act-B each has
      `note` = `"Aisle 7 shelf damaged, waiting on maintenance"` and `changedBy` = `usr-dl-001`;
      second response 400 with `error.code` = `VALIDATION_FAILED` and `error.field` = `note`;
      act-A is still `BLOCKED`; act-A's audit still has exactly 1 entry; no `ActivityStatusChanged`
      event is published by the second request.
      AND a note of exactly 500 characters is accepted (200, stored verbatim).

### AC-3.4 Audit trail is store-scoped  (BR-12)
GIVEN act-A exists in store-001 and has one audit entry (from a bulk `DONE` by `dev-token-store-manager`);
      `act-missing` does not exist
WHEN  `dev-token-store-002-manager` sends `GET /api/activities/act-A/audit`,
      and `dev-token-store-manager` sends `GET /api/activities/act-missing/audit`
THEN  both return 404 with `error.code` = `NOT_FOUND`; the store-002 response body contains no audit data;
      act-A's audit (read as `dev-token-associate`, same store) still returns exactly 1 entry — i.e. any
      authenticated user of the activity's store may read it.

### AC-3.5 Successive bulk updates append entries oldest first  (BR-11, BR-12)
GIVEN act-A exists in store-001, `TODO`
WHEN  `dev-token-store-manager` sends
      `PATCH /api/activities/bulk-status { "status": "BLOCKED", "activityIds": ["act-A"] }` and then
      `dev-token-department-lead` sends `{ "status": "DONE", "activityIds": ["act-A"], "note": "Fixed by night crew" }`
THEN  `GET /api/activities/act-A/audit` returns exactly 2 entries in this order:
      [0] `TODO` → `BLOCKED`, `changedBy` = `usr-sm-001`, `note` = null;
      [1] `BLOCKED` → `DONE`, `changedBy` = `usr-dl-001`, `note` = `"Fixed by night crew"`;
      `[0].occurredAt` ≤ `[1].occurredAt` and the two `id`s differ.

## Out of scope for this sprint
- Audit entries for the single `PATCH /api/activities/{id}`, `POST` and `DELETE` (that endpoint must keep
  writing **no** audit entries).
- Exposing audit data to other modules (no `IActivityQueries` change) or via events.
- Audit retention, paging, or filtering.

## Definition of done
- Every AC has at least one named test that proves it (test name contains `AC3_1` … `AC3_5`), asserting the
  audit entries' fields and counts — not only status codes.
- All sprint-1 and sprint-2 tests still pass.
- `pwsh eng/verify.ps1` prints `VERIFY: PASS`.
