# Generator summary — Sprint 3, iteration 1
Contract: .harness/output/sprint-3-contract.md

## AC self-check
| AC | Status (MET / PARTIAL / NOT MET) | Test(s) that prove it | Notes |
|---|---|---|---|
| AC-3.1 | MET | `ActivityBulkStatusServiceTests.BulkStatus_AC3_1_EachUpdatedActivity_GetsExactlyOneAuditEntry_MatchingItsNewUpdatedAt`; `ActivityBulkStatusRoutesTests.BulkStatus_AC3_1_EachUpdatedActivity_HasExactlyOneAuditEntry_OnTheWire` | Checks the audit is `[]` before, then one entry per activity: `aud-` id, activityId, `TODO`→`DONE`, `usr-sm-001`, `BULK_STATUS_UPDATE`, note null, `occurredAt` == the activity's `updatedAt`. The audit repository holds exactly 2 entries in total (checked through a recording decorator of the real in-memory repository). |
| AC-3.2 | MET | `..ServiceTests.BulkStatus_AC3_2_UnchangedAndFailedItems_GetNoAuditEntry_OnlyTheUpdatedOneDoes`; `..RoutesTests.BulkStatus_AC3_2_UnchangedAndFailedItems_HaveEmptyAuditTrails_OnlyTheUpdatedOneHasAnEntry` | Outcomes FAILED(BUSINESS_RULE_VIOLATION) / UNCHANGED / UPDATED / FAILED(NOT_FOUND); act-A and act-B audits `[]`; act-C has one `TODO`→`BLOCKED` entry; no entry for act-X; exactly 1 entry in total. Also proves the single `PATCH /api/activities/{id}` writes no audit entries (repository empty after the setup PATCHes). |
| AC-3.3 | MET | `..ServiceTests.BulkStatus_AC3_3_HandoverNote_IsStoredOnEveryUpdatedEntry_AndAnOverLongNoteIsRejected`, `..ServiceTests.BulkStatus_AC3_3_NoteOfExactly500Characters_IsAccepted_AndStoredVerbatim`; `..RoutesTests.BulkStatus_AC3_3_HandoverNote_IsOnEveryUpdatedEntry_AndA501CharacterNoteIs400OnNote_ChangingNothing`, `..RoutesTests.BulkStatus_AC3_3_NoteOfExactly500Characters_Returns200_AndIsStoredVerbatim` | Both entries carry the note and `usr-dl-001`. The 501-char request returns 400 `VALIDATION_FAILED` on field `note`. After it act-A is still `BLOCKED` with the same `updatedAt`, its audit still has 1 entry (2 in total), and no `ActivityStatusChanged` was published. A 500-char note is stored verbatim. |
| AC-3.4 | MET | `..ServiceTests.BulkStatus_AC3_4_AuditTrail_IsNotFoundForOtherStoreAndMissingIds_AndReadableByAnyUserOfTheStore`; `..RoutesTests.BulkStatus_AC3_4_AuditOfOtherStoreOrMissingActivity_Is404_AndAnyUserOfTheStoreCanReadIt`, `..RoutesTests.BulkStatus_AC3_4_AuditWithoutBearerToken_Returns401` | Store-002 caller and `act-missing` → 404 `NOT_FOUND` envelope. The store-002 body has no audit data (no `aud-`, `previousStatus`, `BULK_STATUS_UPDATE`). An associate of store-001 reads the 1 entry. Extra test: anonymous → 401. |
| AC-3.5 | MET | `..ServiceTests.BulkStatus_AC3_5_SuccessiveBulkUpdates_AppendEntries_OldestFirst`; `..RoutesTests.BulkStatus_AC3_5_SuccessiveBulkUpdates_ReturnTwoEntries_OldestFirst` | Exactly 2 entries, oldest first: [0] `TODO`→`BLOCKED`, `usr-sm-001`, note null; [1] `BLOCKED`→`DONE`, `usr-dl-001`, "Fixed by night crew". `occurredAt` is in order (strictly increasing in the service test, where the clock is advanced) and the ids differ. |

## Files changed
| Path | Layer | Change (added/modified) | Why |
|---|---|---|---|
| `src/StoreOps.Api/Modules/Activities/ActivityAuditEntry.cs` | Record | added | `ActivityAuditEntry` (with `BulkStatusUpdateSource` const and `ToDto()`) and internal `ActivityAuditEntryDto` (wire shape from the contract; no storeId) |
| `src/StoreOps.Api/Modules/Activities/ActivityAuditRepository.cs` | Repository | added | `IActivityAuditRepository` (`AppendAsync`, `ListByActivityAsync`) + `InMemoryActivityAuditRepository` (ConcurrentDictionary of per-activity ConcurrentQueue, so entries come back oldest first) |
| `src/StoreOps.Api/Modules/Activities/ActivitiesModule.cs` | Module | modified | Registers the audit repository as a singleton |
| `src/StoreOps.Api/Modules/Activities/BulkStatusUpdate.cs` | Record | modified | `BulkStatusUpdateRequest` gains `string? Note = null` (the default keeps existing 2-argument call sites compiling unchanged) |
| `src/StoreOps.Api/Modules/Activities/ActivityService.cs` | Service | modified | Injects `IActivityAuditRepository`. Adds `MaxHandoverNoteLength = 500`, `ValidateHandoverNote` (checked with the rest of the request before any item is touched), `ListAuditAsync(CurrentUser, id, ct)` (store-scoped through `GetVisibleAsync`). The bulk UPDATED path is now `ChangeBulkStatusAsync`: save activity → append audit entry (`Ids.New("aud")`, `occurredAt` = new `updatedAt`) → publish `ActivityStatusChanged` |
| `src/StoreOps.Api/Modules/Activities/ActivityRoutes.cs` | Routes | modified | `group.MapGet("/{id}/audit", ListActivityAuditAsync).WithName("ListActivityAudit")` → `TypedResults.Ok(...)` |
| `tests/StoreOps.Api.Tests/Infrastructure/ModuleTestContext.cs` | Test infra | modified | Passes the new audit repository to `ActivityService`. Adds `RecordingActivityAuditRepository`, a decorator over the real in-memory repository that exposes `All` for "N entries in total" assertions |
| `tests/StoreOps.Api.Tests/Modules/Activities/ActivityBulkStatusServiceTests.cs` | Test | modified | 6 service tests for AC3_1..AC3_5 + `AssertAuditEntry` helper |
| `tests/StoreOps.Api.Tests/Modules/Activities/ActivityBulkStatusRoutesTests.cs` | Test | modified | 7 HTTP tests for AC3_1..AC3_5 (raw JSON wire values). The recording audit repository replaces the singleton via `ConfigureTestServices` |

## Events
none (no new events. `ActivityStatusChanged` is still published once per UPDATED item, with the same fields as before)

## Automated checks
`pwsh eng/verify.ps1` → VERIFY: PASS (220 tests passed, 0 failed)

```
Layer         Covered Coverage Threshold Result
-----         ------- -------- --------- ------
Service layer 261/261 100%     80%       PASS
Route layer   40/40   100%     70%       PASS
Shared        66/69   95.7%    60%       PASS
Overall       540/544 99.3%    70%       PASS
COVERAGE GATE: PASS
VERIFY: PASS
```

## Feedback addressed (retry only)
n/a (iteration 1)

## Known gaps
- Design note, not a failing check: the bulk UPDATED path no longer calls `UpdateAsync`. It now saves, appends the audit entry and publishes in `ChangeBulkStatusAsync`, so the audit write sits between the save and the publish, as the contract specifies. The per-item checks (store scope, associate permission, DONE→BLOCKED) still run first in `GetBulkChangeableAsync`. The published event has the same fields as before, and every sprint-1/2 test passes unchanged.
- The note is stored exactly as sent: not trimmed, and an empty or whitespace-only note is kept as given. The 500 limit counts .NET `string.Length` (UTF-16 code units). The contract does not specify either point.
- `tests/StoreOps.Api.Tests/Infrastructure/ModuleTestContext.cs` had to change because the `ActivityService` constructor gained a parameter. It is not a protected file.
