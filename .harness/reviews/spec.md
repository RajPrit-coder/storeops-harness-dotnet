STATUS: APPROVED
# Spec: Shift handover bulk status update
Feature prompt: "Add shift handover bulk update — PATCH /api/activities/bulk-status lets outgoing shift staff mark multiple operational activities as DONE or BLOCKED in a single request, with partial failure handling (one bad activity must not stop the others) and an audit entry per updated task."

## Goal
At the end of a shift, outgoing store staff (associates, department leads, store managers) must hand over the
state of the floor: which restocking runs, resets and audits are finished and which are stuck. Today they
must PATCH activities one at a time. This feature lets them close out or flag many activities in one request,
reports a per-activity outcome so one bad id never blocks the rest of the handover, and leaves an audit
entry for every activity whose status actually changed so the incoming shift and managers can see who
changed what.

## Scope
- In: `PATCH /api/activities/bulk-status` (target status DONE or BLOCKED only), request validation, per-item
  outcomes (UPDATED / UNCHANGED / FAILED with an error code), store scoping, associate permission rule,
  the existing "DONE cannot be reopened" rule, one `ActivityStatusChanged` event per updated activity
  (so the existing Alerts escalation for BLOCKED fires), an Activities-owned audit trail with one entry per
  updated activity, an optional handover `note`, and `GET /api/activities/{id}/audit` to read the trail.
- Out: bulk changes to priority/category/assignee; target statuses TODO and IN_PROGRESS; audit entries for
  the existing single `PATCH /api/activities/{id}` and for create/delete; a `SHIFT_HANDOVER` alert
  notification; a handover summary report; transactional all-or-nothing mode; asynchronous/queued
  processing.

## API
| Method & path | Request | Success response | Error responses (code → status) |
|---|---|---|---|
| `PATCH /api/activities/bulk-status` | `{ "status": "DONE" \| "BLOCKED", "activityIds": ["act-…", …], "note"?: string }` | `200` `{ "status": "DONE", "requested": n, "updated": u, "unchanged": c, "failed": f, "results": [ { "activityId", "outcome": "UPDATED" \| "UNCHANGED" \| "FAILED", "previousStatus"?, "newStatus"?, "error"?: { "code", "message" } } ] }` — `results` in request order, one per id. Returned whenever the request itself is valid, even if every item fails. | Whole request: `UNAUTHORIZED` → 401 (no/invalid token); `VALIDATION_FAILED` → 400 (status missing, not DONE/BLOCKED, or not a wire value; activityIds missing/empty/>50/blank entry/duplicate; note > 500 chars). Per item (inside a 200, `outcome: FAILED`): `NOT_FOUND` (missing or other store), `FORBIDDEN` (associate not assignee/owner), `BUSINESS_RULE_VIOLATION` (DONE → BLOCKED). |
| `GET /api/activities/{id}/audit` | — | `200` array of `{ "id", "activityId", "previousStatus", "newStatus", "changedBy", "source": "BULK_STATUS_UPDATE", "note", "occurredAt" }`, oldest first; `[]` when none | `UNAUTHORIZED` → 401; `NOT_FOUND` → 404 (missing or other store) |

## Business rules
- BR-1 Both endpoints require a bearer token; without one → 401 `UNAUTHORIZED` and nothing changes.
- BR-2 `status` is required and must be `DONE` or `BLOCKED`. `TODO`/`IN_PROGRESS`/missing → 400
  `VALIDATION_FAILED` with field `status`; a non-wire value (e.g. `"done"`) → 400 `VALIDATION_FAILED`
  (shared enum converter). No activity changes, no events.
- BR-3 `activityIds` is required, contains 1–50 entries, every entry non-blank, no duplicates; otherwise
  400 `VALIDATION_FAILED` with field `activityIds`. No activity changes, no events.
- BR-4 For a valid request the response is 200 with one result per requested id in request order, and the
  counts `requested = updated + unchanged + failed`.
- BR-5 An activity already in the target status is `UNCHANGED`: not saved (its `updatedAt` stays), no event.
- BR-6 Each `UPDATED` activity is saved with the new status and `updatedAt`, and then exactly one
  `ActivityStatusChanged` (existing event, Activities.Contracts) is published for it with
  `PreviousStatus`, `NewStatus`, `ChangedBy` = caller. `UNCHANGED` and `FAILED` items publish nothing.
  (Alerts already turns `→ BLOCKED` into an `ESCALATION` notification for every active store manager.)
- BR-7 Partial failure: each id is processed independently. A failing item never stops, skips or rolls back
  any other item, and the request still returns 200 when every item fails. Per-item checks run in this
  order and the first that applies decides the outcome: NOT_FOUND (BR-8) → FORBIDDEN (BR-9) →
  UNCHANGED (BR-5) → BUSINESS_RULE_VIOLATION (BR-10) → UPDATED.
- BR-8 Store scoping: an id that does not exist or belongs to another store → `FAILED` with `NOT_FOUND`;
  that activity is untouched.
- BR-9 An `ASSOCIATE` may only bulk-update activities whose `assigneeId` or `ownerId` is the caller; any
  other activity → `FAILED` with `FORBIDDEN`, untouched. `DEPARTMENT_LEAD`, `STORE_MANAGER` and
  `REGIONAL_MANAGER` may update any activity in their own store.
- BR-10 A `DONE` activity cannot be reopened: `DONE` → `BLOCKED` → `FAILED` with
  `BUSINESS_RULE_VIOLATION`, activity stays `DONE`. (`BLOCKED` → `DONE` is allowed.)
- BR-11 Every `UPDATED` activity gets exactly one audit entry (Activities module, not a new module):
  `id` (`aud-…`), `activityId`, `storeId`, `previousStatus`, `newStatus`, `changedBy` = caller,
  `source` = `BULK_STATUS_UPDATE`, `note`, `occurredAt` (= the activity's new `updatedAt`). `UNCHANGED`
  and `FAILED` items get no audit entry.
- BR-12 `GET /api/activities/{id}/audit` returns that activity's audit entries oldest first, to any
  authenticated user of the activity's store; missing or other-store id → 404 `NOT_FOUND`.
- BR-13 `note` is optional, at most 500 characters (longer → 400 `VALIDATION_FAILED` field `note`, nothing
  changes, no audit entry), and is copied into the audit entry of every activity updated by that request
  (`null` when omitted).

## Architecture impact
| Module | Layer | New / changed type | Why |
|---|---|---|---|
| Activities | Record | `BulkStatusUpdateRequest` (internal sealed record: `ActivityStatus? Status`, `IReadOnlyList<string?>? ActivityIds`, later `string? Note`) | request body; nullable so the service owns validation |
| Activities | Record | `BulkStatusUpdateResponse`, `BulkStatusItemResult`, `BulkStatusItemError`, `BulkStatusOutcome` enum (`UPDATED UNCHANGED FAILED`) | response shape |
| Activities | Service | `ActivityService.BulkUpdateStatusAsync(CurrentUser, BulkStatusUpdateRequest, CancellationToken)` | all rules BR-2…BR-11; catches `AppError` per item to build FAILED results |
| Activities | Service | `ActivityService.ListAuditAsync(CurrentUser, string id, CancellationToken)` | BR-12 |
| Activities | Routes | `ActivityRoutes`: `PATCH /bulk-status`, `GET /{id}/audit` | binding + `TypedResults.Ok` only |
| Activities | Record | `ActivityAuditEntry` (internal sealed record, `ToDto()`), `ActivityAuditEntryDto` (internal sealed record) | audit trail owned by Activities |
| Activities | Repository | `IActivityAuditRepository` + `InMemoryActivityAuditRepository` (`AppendAsync`, `ListByActivityAsync`) | data access only |
| Activities | Module | `ActivitiesModule.AddActivitiesModule` registers the audit repository (singleton) | composition |

Events published: `ActivityStatusChanged` (existing, in Activities.Contracts) → consumed by Alerts
(existing `ActivityBlockedSubscriber`, unchanged). No new events: the audit trail lives inside the
publishing module, so no cross-module write is involved.
Cross-module reads: none (the caller's role and store come from `CurrentUser`).
No new module dependencies; no protected files touched.

## Sprints
| Sprint | Title | ACs | Depends on |
|---|---|---|---|
| 1 | Bulk status endpoint: contract, validation, happy path | AC-1.1 … AC-1.6 (BR-1…BR-6) | — |
| 2 | Per-item failure handling: store scope, permissions, reopen rule | AC-2.1 … AC-2.6 (BR-7…BR-10) | 1 |
| 3 | Audit trail per updated activity + handover note | AC-3.1 … AC-3.5 (BR-11…BR-13) | 1, 2 |
