# Skill: architecture-principles (StoreOps)

## Purpose
The non-negotiable StoreOps architecture rules (spec §3.5), each with the failure mode it prevents and the
automated check that enforces it. Read by: every agent.

## R1 — Module boundary (prevents F1: importing another module's repository)
- A module may use another module **only** through that module's `Contracts` namespace.
- Cross-module **reads** go through the target's query interface, which its Service implements:
  `IStaffDirectory` (Staff), `IProgrammeQueries` (Programmes), `IActivityQueries` (Activities).
- Contract interfaces contain **read methods only** (`Find*`, `Get*`, `List*`, `Exists*`, `Count*`) and the
  implementation must not write or publish. A method named `FindAndMarkDoneAsync` is still a write → FAIL.
- Allowed dependencies (anything else fails `Module_DependsOnlyOnDeclaredModules`):

| Module | May depend on |
|---|---|
| Staff | — |
| Programmes | Staff |
| Activities | Staff, Programmes |
| Alerts | Staff, Activities |
| Reports | Activities, Programmes, Staff |

- No cycles. **Nothing may depend on Reports.** Shared depends on no module.
- ❌ `services.GetRequiredService<Activities.IActivityRepository>()` inside Reports — even inside a lambda.
- ✅ `activityQueries.ListByStoreAsync(storeId, ct)` via the injected `IActivityQueries`.
- Enforced by: `ModuleBoundaryTests` (IL-level, sees inside lambdas and async methods).

## R2 — Event bus only (prevents F4: writing to a sibling module directly)
- A side effect in another module is triggered **only** by `await eventBus.PublishAsync(new XHappened(...), ct)`.
- The event record lives in the **publisher's** `Contracts` (never in Shared), implements `IIntegrationEvent`,
  and carries everything the subscriber needs (ids, store, before/after values, who, `OccurredAt`).
- Publish **after** the repository save succeeds, once per state change.
- Subscribers are `internal sealed class <Name>Subscriber : IEventSubscriber<TEvent>` in the **consuming**
  module, registered with `services.AddEventSubscriber<TEvent, TSubscriber>()`, and delegate in one line to
  their own module's service.
- Importing `NotificationService` or `ReportService` anywhere outside their module = hard-gate failure.
- Enforced by: `EventOnlyModule_IsReachedByOthers_OnlyThroughItsEvents`, `IntegrationEvents_AreDeclaredInThePublishersContracts`,
  `Subscribers_DelegateToTheirOwnService`.

## R3 — Error contract (prevents F2: raw throws)
- Throw only: `ValidationError` (400, with field), `UnauthorizedError` (401), `ForbiddenError` (403),
  `NotFoundError` (404), `ConflictError` (409), `BusinessRuleError` (422) from `StoreOps.Api.Shared.Errors`.
- Never `new Exception/InvalidOperationException/ArgumentException/...`, never `ArgumentNullException.ThrowIfNull`,
  never `ObjectDisposedException.ThrowIf`, never `Results.Problem/BadRequest/NotFound/...` in a route.
- All errors reach the client as `{ "error": { "code", "message", "statusCode", "field"? } }`.
- Enforced by: build (RS0030 banned APIs) and `ErrorContractTests`, `ModuleCode_ProducesErrorsOnlyAsAppErrors`.

## R4 — Layer separation: Routes → Service → Repository, no skipping
- **Routes** (`internal static class <X>Routes`): bind input, call **their own module's service**, return
  `TypedResults.Ok/Created/NoContent/Accepted`. No `if` on domain state, no query contracts, no event bus.
- **Service** (`internal sealed class <X>Service`): all business rules, validation of business meaning,
  store scoping, permission checks, publishing events. The only layer that may use a repository.
- **Repository** (`internal interface I<X>Repository` + `internal sealed class InMemory<X>Repository`):
  data access only — no HTTP, no `HttpClient`, no event bus, no other modules, no business rules.
- Endpoints are mapped only in `*Routes`; `Program.cs` only calls `Add<X>Module` / `Map<X>Module`.
- Every type in a module must have a recognised role (see coding-conventions §Naming); an unrecognised
  class fails `EveryModuleType_HasAKnownRole`.
- Enforced by: `LayeringTests`.

## R5 — Read-only reports
Reports aggregates Activities/Programmes/Staff data through their query contracts and writes only its own
`Report` records. It never writes to, or triggers writes in, another module.

## R6 — Store scoping (StoreOps business rule)
A caller sees and changes only their own store's data. A resource in another store is reported as
`NotFoundError` (not 403), so its existence is not leaked. Permission failures inside the same store are
`ForbiddenError`.

## Protected files
No agent changes `CLAUDE.md`, `.harness/agents/`, `.harness/skills/`, `eng/`, `tests/StoreOps.Api.Tests/Architecture/`,
`BannedSymbols.txt`, `.editorconfig`, `stylecop.json`, `Directory.*.props`, `coverage.runsettings`, `.github/`.
Weakening a check to make code pass is the worst possible failure — the Evaluator fails it automatically (HG-A2).
