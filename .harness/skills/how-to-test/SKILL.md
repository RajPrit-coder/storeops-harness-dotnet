# Skill: how-to-test (StoreOps)

## Purpose
How to write StoreOps tests that prove the **business rule**, not just the status code (failure mode F3),
and that meet the coverage thresholds. Read by: Generator.

## Where tests go (mirror src/)
| Code under test | Test file | Style |
|---|---|---|
| `Modules/<M>/<X>Service.cs` | `tests/StoreOps.Api.Tests/Modules/<M>/<X>ServiceTests.cs` | service test |
| `Modules/<M>/<X>Routes.cs` | `tests/StoreOps.Api.Tests/Modules/<M>/<X>RoutesTests.cs` | HTTP test |
Add to the existing file when it exists. Do not touch `tests/StoreOps.Api.Tests/Architecture/`.

## Naming — carry the AC id
`<Method>_AC<sprint>_<n>_<Scenario>_<Expected>` — e.g. `BulkStatus_AC1_3_OtherStoreActivity_IsReportedNotFound_AndUnchanged`.
The Evaluator runs `dotnet test --filter "FullyQualifiedName~AC1_3"` for every AC: no matching test = hard-gate failure.

## Service tests (business rules + events)
```csharp
public sealed class ActivityServiceTests
{
    private readonly ModuleTestContext _ctx = new();   // real services, fresh in-memory repos,
                                                        // RecordingEventBus (_ctx.Events), ManualTimeProvider (_ctx.Time)
    [Fact]
    public async Task X_AC1_2_...()
    {
        var a = await _ctx.Activities.CreateAsync(TestUsers.Associate, new CreateActivityRequest("Restock", null, null, null, null, null, null, null), default);
        _ctx.Events.Clear();                            // only assert events caused by the call under test

        var result = await _ctx.Activities.<NewMethod>(TestUsers.StoreManager, ..., default);

        Assert.Equal(ActivityStatus.Done, (await _ctx.Activities.FindByIdAsync(a.Id, default))!.Status);   // state
        var evt = Assert.Single(_ctx.Events.Of<ActivityStatusChanged>());                                  // event
        Assert.Equal((ActivityStatus.Todo, ActivityStatus.Done), (evt.PreviousStatus, evt.NewStatus));
    }
}
```
Callers: `TestUsers.StoreManager / DepartmentLead / Associate / OtherStoreManager` (store-002).
Errors: `await Assert.ThrowsAsync<ForbiddenError>(() => …)`.

## HTTP tests (routing, wire format, auth, envelope)
```csharp
using var api = new TestApi();                                   // fresh app per test
var client = api.As(StaffSeedData.StoreManagerToken);            // bearer token; api.Anonymous for 401 tests
using var response = await client.PatchJsonAsync("/api/activities/bulk-status", new { status = "DONE", activityIds = new[] { id } });
Assert.Equal(HttpStatusCode.OK, response.StatusCode);
var body = await response.ReadJsonAsync<SomeDto>();
await response.AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);   // for error envelopes
```
Helpers in `tests/StoreOps.Api.Tests/Infrastructure/`: `GetJsonAsync`, `PostJsonAsync`, `PatchJsonAsync`,
`DeleteAsync`, `ReadJsonAsync`, `AssertErrorAsync(status, code, field?)`. To check raw wire values
(`"DONE"`), parse with `JsonDocument`.

## The rules the Evaluator checks
1. **Assert the outcome, not just the status.** Every success test checks the resulting state; every
   test of a state change also checks the event(s) in `_ctx.Events`.
2. **Rejections prove nothing changed:** state is as before **and** `Assert.Empty(_ctx.Events.Published)`
   (or the affected item is unchanged on a follow-up GET).
3. **Partial failure:** assert each item's outcome AND that successful items really changed AND failed
   items really did not.
4. **Determinism:** the `ManualTimeProvider` is frozen — when order or "later than" matters, advance it
   (`_ctx.Time.Now = _ctx.Time.Now.AddSeconds(1)`). No `Thread.Sleep`, no real clock, no shared static state.
5. **Right level:** business rules in service tests; routing, JSON shape, auth (401/403) and envelope in HTTP tests.

## Coverage thresholds (spec §3.6, enforced by `eng/check-coverage.ps1`)
Service layer ≥ 80% · Route layer ≥ 70% · Shared ≥ 60% · Overall ≥ 70%. Every new service method and route
handler needs at least one test that executes it.
