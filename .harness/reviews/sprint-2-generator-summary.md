# Generator summary — Sprint 2, iteration 1
Contract: .harness/output/sprint-2-contract.md

## AC self-check
| AC | Status (MET / PARTIAL / NOT MET) | Test(s) that prove it | Notes |
|---|---|---|---|
| AC-2.1 | MET | `ActivityBulkStatusServiceTests.BulkStatus_AC2_1_MissingAndOtherStoreIds_FailAsNotFound_AndTheOwnStoreActivityIsStillUpdated`; `ActivityBulkStatusRoutesTests.BulkStatus_AC2_1_MissingAndOtherStoreIds_FailAsNotFound_Returns200_AndOwnStoreActivityIsDone` | Asserts counts 3/1/0/2, per-item order and codes, act-B still TODO with unchanged `updatedAt` (HTTP: GET as store-002 manager), act-A DONE, exactly one `ActivityStatusChanged` (act-A). |
| AC-2.2 | MET | `..ServiceTests.BulkStatus_AC2_2_Associate_UpdatesAssignedAndOwnedActivities_AndIsForbiddenOnOthers`; `..RoutesTests.BulkStatus_AC2_2_Associate_UpdatesAssignedAndOwned_AndGetsForbiddenItemForOthers` | act-A (assignee usr-as-001) and act-C (owner usr-as-001) UPDATED; act-B FAILED/FORBIDDEN, still TODO with unchanged `updatedAt`; two events [act-A, act-C], `ChangedBy` = usr-as-001. |
| AC-2.3 | MET | `..ServiceTests.BulkStatus_AC2_3_DepartmentLeadAndRegionalManager_MayUpdateAnyActivityOfTheirStore`; `..RoutesTests.BulkStatus_AC2_3_DepartmentLeadAndRegionalManager_UpdateUnassignedActivitiesOfTheirStore` | Both 200 with updated=1/failed=0; act-A BLOCKED, act-B DONE; events `ChangedBy` usr-dl-001 / usr-rm-001. |
| AC-2.4 | MET | `..ServiceTests.BulkStatus_AC2_4_DoneActivity_CannotBeBlocked_AndRaisesNoEscalation_WhileBlockedCanBeDone`; `..RoutesTests.BulkStatus_AC2_4_DoneActivityToBlocked_IsBusinessRuleFailure_WithoutEscalation_AndBlockedToDoneIsUpdated` | act-A FAILED/BUSINESS_RULE_VIOLATION, still DONE with unchanged `updatedAt`; act-C TODO→BLOCKED; one event (act-C); no ESCALATION with sourceActivityId act-A (HTTP: real bus + `GET /api/alerts`; service: published event delivered to Alerts). Second request: act-B BLOCKED→DONE UPDATED with its event. |
| AC-2.5 | MET | `..ServiceTests.BulkStatus_AC2_5_EveryItemFailing_ReturnsAllFailed_AndChangesNothing`; `..RoutesTests.BulkStatus_AC2_5_EveryItemFailing_StillReturns200_WithAllFailed_AndNothingChanges` | 200, counts 2/0/0/2, both NOT_FOUND with non-empty `error.message`, act-B still TODO, no events. |
| AC-2.6 | MET | `..ServiceTests.BulkStatus_AC2_6_CheckOrder_NotFoundBeatsForbidden_AndForbiddenBeatsUnchanged`; `..ServiceTests.BulkStatus_AC2_6_CheckOrder_ForbiddenBeatsBusinessRule_ForAssociateBlockingAnothersDoneActivity`; `..RoutesTests.BulkStatus_AC2_6_Associate_GetsForbiddenBeforeUnchanged_AndNotFoundBeforeForbidden` | act-A (DONE, not the associate's) → FORBIDDEN not UNCHANGED; act-B (store-002) → NOT_FOUND not FORBIDDEN; unchanged=0, failed=2, no events, status and `updatedAt` unchanged. Extra test also proves FORBIDDEN precedes BUSINESS_RULE_VIOLATION. |

## Files changed
| Path | Layer | Change (added/modified) | Why |
|---|---|---|---|
| `src/StoreOps.Api/Modules/Activities/ActivityService.cs` | Service | modified | `ApplyBulkStatusAsync` now guards the BR-7 per-item checks with `catch (AppError)` and maps the error to a `FAILED` result (`previousStatus`/`newStatus` null, `error = { Code, Message }`). New private `GetBulkChangeableAsync` runs the checks in BR-7 order: store-scoped lookup (`NotFoundError`), associate assigned/owner rule (`ForbiddenError`), already-in-target (returned → UNCHANGED), DONE→BLOCKED (`BusinessRuleError`). Only the checks are inside the `try`; the save/publish happens outside it, so an error after a save is never disguised as a per-item failure. Doc comment on `BulkUpdateStatusAsync` updated. |
| `tests/StoreOps.Api.Tests/Modules/Activities/ActivityBulkStatusServiceTests.cs` | Test | modified | 7 service tests for AC-2.1 … AC-2.6 plus helpers (`BulkAsync`, `SetStatusAsync`, `AssertFailed`, local `RegionalManager` caller). |
| `tests/StoreOps.Api.Tests/Modules/Activities/ActivityBulkStatusRoutesTests.cs` | Test | modified | 6 HTTP tests for AC-2.1 … AC-2.6 (wire values `FAILED`, null statuses, error code/message), helpers `AssertCounts`, `AssertFailedItem`; `CreateAsync` gained optional creator client and `assigneeId`. |

`BulkStatusUpdate.cs` (records) needed no change — `BulkStatusItemResult` already has nullable statuses and `BulkStatusItemError(Code, Message)`.

## Events
| Event | Publisher | Subscriber(s) |
|---|---|---|
| `ActivityStatusChanged` (existing, unchanged) | Activities (`ActivityService.UpdateAsync`, once per UPDATED item; never for FAILED/UNCHANGED items) | Alerts (`ESCALATION` on → BLOCKED, existing) |

No new events.

## Automated checks
`pwsh eng/verify.ps1` → VERIFY: PASS (207 tests passed, 0 failed; build, format, architecture tests all green)

```
Layer         Covered Coverage Threshold Result
-----         ------- -------- --------- ------
Service layer 233/233 100%     80%       PASS
Route layer   38/38   100%     70%       PASS
Shared        66/69   95.7%    60%       PASS
Overall       503/507 99.2%    70%       PASS
```

## Feedback addressed (retry only)
n/a — iteration 1.

## Known gaps
- none. Note: the single `PATCH /api/activities/{id}` permission rules are untouched (out of scope); the associate rule applies to the bulk endpoint only.
