# Generator summary — Sprint 1, iteration 1
Contract: .harness/output/sprint-1-contract.md

## AC self-check
| AC | Status (MET / PARTIAL / NOT MET) | Test(s) that prove it | Notes |
|---|---|---|---|
| AC-1.1 | MET | `ActivityBulkStatusServiceTests.BulkStatus_AC1_1_TwoTodoActivities_AreBothDone_InRequestOrder_WithOneEventEach`; `ActivityBulkStatusRoutesTests.BulkStatus_AC1_1_TwoActivitiesMarkedDone_Returns200PerItemResults_AndBothAreDone` | Asserts counts, request-ordered results, raw wire values (`DONE`, `UPDATED`, `TODO`, `error: null`), stored status + `updatedAt >= createdAt` (service test advances the clock and asserts strictly later), exactly two `ActivityStatusChanged` (Todo→Done, `usr-sm-001`, store-001). HTTP test reaching the bulk handler proves `/bulk-status` wins over `/{id}`. |
| AC-1.2 | MET | `..ServiceTests.BulkStatus_AC1_2_DepartmentLeadBlocksActivity_PublishesBlockedEvent_ThatRaisesEscalation`; `..RoutesTests.BulkStatus_AC1_2_DepartmentLeadBlocks_RaisesExactlyOneEscalationForTheStoreManager` | HTTP test runs on the real bus: no alert before, exactly one `ESCALATION`/`IN_APP` alert for act-A after, one event with `ChangedBy = usr-dl-001`. Alerts unchanged. |
| AC-1.3 | MET | `..ServiceTests.BulkStatus_AC1_3_TargetOtherThanDoneOrBlocked_IsRejectedOnStatus_AndNothingChanges` (IN_PROGRESS, TODO, null); `..RoutesTests.BulkStatus_AC1_3_TargetNotDoneOrBlocked_Returns400_AndActivityIsUnchanged` (cases a–d) | 400 `VALIDATION_FAILED`, `field = status` for a/b/c; (d) `"done"` rejected by the shared converter (field not asserted, per AC). Status and `updatedAt` unchanged, no event. |
| AC-1.4 | MET | `..ServiceTests.BulkStatus_AC1_4_InvalidActivityIds_RejectsWholeRequest_AndValidFirstIdIsNotUpdated` (missing, empty, blank, null entry, duplicate, 51); `..ServiceTests.BulkStatus_AC1_4_ExactlyFiftyDistinctIds_AreAllUpdated`; `..RoutesTests.BulkStatus_AC1_4_InvalidActivityIds_Returns400OnActivityIds_AndFirstValidIdIsNotUpdated` (a–e); `..RoutesTests.BulkStatus_AC1_4_FiftyDistinctExistingIds_Returns200_WithFiftyUpdated` | Whole request validated before any activity is touched; act-A (valid, listed first) stays TODO; no event. 50 ids → 200, `updated = 50`, all 50 DONE, 50 events. |
| AC-1.5 | MET | `..ServiceTests.BulkStatus_AC1_5_ActivityAlreadyInTarget_IsUnchanged_NotTouched_AndGetsNoEvent`; `..RoutesTests.BulkStatus_AC1_5_AlreadyBlockedActivity_IsUnchanged_AndOnlyTheOtherIsUpdated` | Counts 2/1/1/0; results[0] UNCHANGED BLOCKED→BLOCKED, results[1] UPDATED TODO→BLOCKED; act-A `updatedAt` unchanged (service test advances the clock first); exactly one event, for act-B. |
| AC-1.6 | MET | `..RoutesTests.BulkStatus_AC1_6_WithoutBearerToken_Returns401_AndActivityIsUnchanged` | Default `TestApi` (Testing env, tokens required), anonymous client → 401 `UNAUTHORIZED`; act-A still TODO; no event. |

## Files changed
| Path | Layer | Change (added/modified) | Why |
|---|---|---|---|
| `src/StoreOps.Api/Modules/Activities/BulkStatusUpdate.cs` | Record | added | `internal sealed record`s `BulkStatusUpdateRequest` (nullable fields), `BulkStatusUpdateResponse`, `BulkStatusItemResult`, `BulkStatusItemError` |
| `src/StoreOps.Api/Modules/Activities/Contracts/ActivityContracts.cs` | Contract | modified | `public enum BulkStatusOutcome { Updated, Unchanged, Failed }` — see Known gaps for why it is here and not `internal` in the module root |
| `src/StoreOps.Api/Modules/Activities/ActivityService.cs` | Service | modified | `BulkUpdateStatusAsync(CurrentUser, BulkStatusUpdateRequest, CancellationToken)`: validates status (DONE/BLOCKED, field `status`) and ids (1–50, non-blank, distinct, field `activityIds`) up front with `ValidationError`; then loops ids one at a time via existing `GetVisibleAsync`; same status → UNCHANGED (no save, no event); otherwise delegates to existing `UpdateAsync` (save, then one `ActivityStatusChanged`). `MaxBulkActivityIds = 50` constant. |
| `src/StoreOps.Api/Modules/Activities/ActivityRoutes.cs` | Routes | modified | `group.MapPatch("/bulk-status", …).WithName("BulkUpdateActivityStatus")`; handler is a one-line `TypedResults.Ok(await service.BulkUpdateStatusAsync(...))`, no `if` |
| `tests/StoreOps.Api.Tests/Modules/Activities/ActivityBulkStatusServiceTests.cs` | Test | added | Business-rule tests AC-1.1–1.5 with `ModuleTestContext` (state + `RecordingEventBus` + manual clock) |
| `tests/StoreOps.Api.Tests/Modules/Activities/ActivityBulkStatusRoutesTests.cs` | Test | added | HTTP tests AC-1.1–1.6 (routing, wire format, envelope, auth); events captured by a test-only extra `IEventSubscriber<ActivityStatusChanged>` registered on the real in-memory bus, so Alerts still runs |

## Events
| Event | Publisher | Subscriber(s) |
|---|---|---|
| `ActivityStatusChanged` (existing, reused; one per actually changed activity) | `ActivityService.BulkUpdateStatusAsync` → `UpdateAsync` | Alerts `ActivityBlockedSubscriber` (existing, unchanged — escalates on BLOCKED) |

## Automated checks
`pwsh eng/verify.ps1` → VERIFY: PASS (1st run; 194 tests passed, 27 of them new)

```
Layer         Covered Coverage Threshold Result
-----         ------- -------- --------- ------
Service layer 220/220 100%     80%       PASS
Route layer   38/38   100%     70%       PASS
Shared        66/69   95.7%    60%       PASS
Overall       488/494 98.8%    70%       PASS
COVERAGE GATE: PASS
```

## Feedback addressed (retry only)
n/a (iteration 1)

## Known gaps
- **Deviation from contract placement:** the contract lists `BulkStatusOutcome` as `internal` in the module root.
  An `internal enum` there has no recognised role and would fail `LayeringTests.EveryModuleType_HasAKnownRole`
  ("everything else must be a record"). Following `coding-conventions` §Placement ("Response DTO / enum / event →
  Contracts"), it is a `public enum` in `Activities/Contracts/ActivityContracts.cs`. The four records stay
  `internal` in `BulkStatusUpdate.cs` as the contract specifies.
- Out of scope, not handled yet (sprint 2): an unknown / other-store id, or a DONE activity targeted with BLOCKED,
  currently propagates the existing `NotFoundError` / `BusinessRuleError` and aborts the request at that item
  (items before it stay updated). The per-item loop (`ApplyBulkStatusAsync`) is the place to add the per-item
  `AppError` catch → `FAILED` result.
- Duplicate detection is ordinal on the raw id strings (no trimming), consistent with how single-activity
  lookups treat ids.
