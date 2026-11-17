# Bootstrap corrections log

Spec §2.3 Step 3 says to review the generated StoreOps code against Section 3, correct any deviations, and *"keep track of the corrections you make … they tell you exactly what your Evaluator needs to check and what hard gates to set."* This is that record. Each entry gives what the first generation got wrong, the spec clause it broke, the fix, and the check that now catches it.

The first pass was generated from the bootstrap prompt alone, without Section 3. Most deviations come from that, which is itself a finding: **the prompt is not enough context**. The Planner and Generator need §3.3–3.6 in their skill files, not just a pointer to "Section 3.6".

## A. Spec conformance (what the prompt alone could not convey)

| # | Deviation in the first generation | Spec | Correction | Now caught by |
|---|---|---|---|---|
| A1 | It invented nine endpoints, including `GET /api/staff` and `GET /api/reports/metrics`, and omitted GET/PATCH/DELETE `/api/activities/:id` and `POST /api/programmes/:id/members` | §3.6 | Implemented exactly the nine §3.6 endpoints. Staff and reports have none | `*RoutesTests` per module; `StaffAuthenticationTests.StaffModule_ExposesNoEndpoints` |
| A2 | Invented enums (`Planned/Completed/Cancelled`, `Severity`, `Draft/Active/Closed`) | §3.3 | Spec values: `TODO/IN_PROGRESS/DONE/BLOCKED`, priority, category, roles, `AlertType`, channel, report types | `StoreOpsJsonTests`; routes tests assert raw JSON values such as `"TODO"` and `"DEPARTMENT_LEAD"` |
| A3 | Enums serialised as `"InProgress"` | §3.3 (SCREAMING_SNAKE values) | `JsonStringEnumConverter(SnakeCaseUpper)`. Query filters parse the same wire names | `ActivityRoutesTests.List_FiltersByProgrammeAndStatusWireValue` |
| A4 | No authentication, although §3.6 says "authenticated store", "authenticated user" and "owner or store manager only" | §3.3 staff, §3.6 | A bearer-token handler in Staff, a fallback "authenticated" policy, a `CurrentUser` binder, store-scoped reads (another store's data returns 404), and owner-or-store-manager delete (403) | `StaffAuthenticationTests`; `ActivityServiceTests.Delete_*`; `Get_ActivityOfAnotherStore_ThrowsNotFound` |
| A5 | Reports had an endpoint and maintained event-fed counters | §3.6 ("reports is added later"), §3.3 types | Reports holds only `Report`, `ReportType` and `ReportStatus`, plus a read-only `GenerateStoreSummaryAsync` and no route | `ReportServiceTests` |
| A6 | Staff exposed a list endpoint | §3.6 ("staff is auth-only") | Removed. Staff is the authentication and read-only directory | as A1 |
| A7 | Tests organised by kind (Endpoints/, SharedKernel/) | §7 ("tests mirroring the module structure") | `tests/…/Modules/<Module>/`, `Shared/`, `Architecture/` | n/a (structure) |
| A8 | Folder and namespace `SharedKernel` (renamed to dodge analyzer CA1716) | §7 `shared/` | Restored to `Shared`, with CA1716 switched off for that folder only and the reason recorded | n/a |
| A9 | No StyleCop | §3.2 Option D | StyleCop.Analyzers, `stylecop.json`, with each disabled rule justified in `.editorconfig` | build (warnings are errors) |
| A10 | No coverage thresholds | §3.6 | `coverage.runsettings` + `eng/check-coverage.ps1` per layer | `eng/verify.ps1` |

## B. Architecture rule violations (the §2 failure modes, reproduced by the generator)

| # | Violation | Rule (§3.5) / failure mode (§2) | Correction | Now caught by |
|---|---|---|---|---|
| B1 | `ReportProjectionHandler` (an event subscriber) wrote straight to `IReportRepository`, skipping `ReportService` | Layer separation | Subscribers are inbound adapters, like routes, and delegate to the service (`ActivityBlockedSubscriber` → `NotificationService`) | `LayeringTests.OnlyServices_UseRepositories`: only `*Service` and `*Module` may reference a repository |
| B2 | The layering test checked only `*Routes` → repository, so subscribers and the auth handler were unchecked | Layer separation | Rule inverted to an allow-list (only services may use repositories) | as B1. Verified with a probe subscriber in Reports |
| B3 | Contract interfaces were not checked for write methods, so a module could expose `UpdateX` to others and bypass the bus | Event bus only; read-only reports; staff read-only | Every module's `Contracts` interface must be query-only (`Find`/`Get`/`List`/`Exists`/`Count`), and its DTOs immutable | `ModuleContracts_ExposeReadOnlyLookupsAndImmutableData` |
| B4 | Only Alerts was protected from direct imports. §3.5 names `ReportService` too | Event bus only | Alerts and Reports are both "event-only" targets | `EventOnlyModule_IsReachedByOthers_OnlyThroughItsEvents` |
| B5 | No check that repositories stay data-access only | Layer separation ("repositories must not call external services") | Repositories may not use `Microsoft.AspNetCore`, `System.Net.Http`, `IEventBus`, other modules, or their own service or routes | `LayeringTests.Repositories_AreDataAccessOnly` |
| B6 | `Shared` could depend on a module (a hidden cycle), and `Contracts` could leak module internals | No circular imports | Added both rules | `Shared_DoesNotDependOnAnyModule`, `ModuleContracts_DoNotLeakModuleInternals` |

## C. Error contract

| # | Deviation | Correction | Now caught by |
|---|---|---|---|
| C1 | 401/403 from auth, 404 for unknown routes and 405 for wrong methods returned empty bodies | `UseErrorEnvelopeForBareStatusCodes` (status-code pages) renders the same envelope | `ErrorHandlingTests.UnknownRoute_*`, `UnsupportedMethod_*`, `ActivityRoutesTests.Endpoints_WithoutToken_*` |
| C2 | Malformed JSON returned a bare 400 outside Development | `RouteHandlerOptions.ThrowOnBadRequest = true` → `BAD_REQUEST` envelope | `Post_WithMalformedJson_*`, `Post_WithUnknownEnumValue_*` |
| C3 | `"field": null` was emitted on errors without a field | `JsonIgnore(WhenWritingNull)` | `ErrorWithoutField_OmitsFieldFromTheEnvelope` |
| C4 | No 401/403 error types | Added `UnauthorizedError` and `ForbiddenError` | `AppErrorSubtypes_CarryCodeAndStatus` |

## D. Gate defects found while verifying the gates

| # | Defect | Lesson for the Evaluator | Fix |
|---|---|---|---|
| D1 | The coverage gate first reported Service/Routes/Shared as **0/0 lines = PASS**. Coverlet writes paths relative to a source root, so the patterns never matched | **A gate that matches nothing must FAIL, never pass.** Every automated hard gate needs a "did I actually inspect anything?" check | Paths are normalised, and an empty layer now fails |
| D2 | A test was flaky: two activities with the same timestamp from a frozen clock were ordered by random id | Run the suite several times when judging determinism; frozen clocks hide ordering assumptions | The test advances the clock. The suite passed 10 runs in a row |

## E. Second review: the gates themselves were too weak

An independent reviewer wrote 18 probe violations, and **all of them passed every check** (`VERIFY: PASS`). Root causes and fixes:

| # | Gap | Fix |
|---|---|---|
| E1 | NetArchTest could not see references made only inside an async lambda (a closure's nested state machine) | Replaced it with a Mono.Cecil IL model that charges every reference to the outermost type |
| E2 | `Shared` was an unchecked back door: a write port or an event declared there hid the cross-module dependency | Events must live in the publisher's `Contracts`; module types may implement only `IIntegrationEvent` and `IEventSubscriber<T>` from Shared |
| E3 | "Read-only contract" was only a method-name check (`FindAndMarkDoneAsync` passed) | `ReadOnlyContractTests` follows each contract method's IL (helpers, state machines, lambdas) and fails on repository writes or event publishing |
| E4 | Nothing outside `Modules/` was checked (`Program.cs` endpoints, a `Features/` folder) | Every type must be in Modules or Shared, or be `Program`; only `*Routes` may map endpoints; `Program` may only call `*Module` registrations |
| E5 | Business logic and non-envelope errors in routes (`Results.Problem`, `BadRequest(obj)`) | Routes may use only their own service, records, DTOs, `CurrentUser` and `StoreOpsJson`; only success `Results` factories are allowed |
| E6 | A class named `ActivityEndpoints` or `ActivityStore` escaped every name-based rule | Every module type must fit a known role, by name and by shape |
| E7 | Static throw helpers (`ObjectDisposedException.ThrowIf`) and unbanned types (`FormatException`) in `Program.cs` | The IL scan covers the whole assembly and also flags static calls on non-AppError exception types |
| E8 | Body enums accepted C# names and casings (`DepartmentLead`, `departmentlead`) | A strict converter accepts the exact SCREAMING_SNAKE value only |
| E9 | 415/413/408 were labelled `BAD_REQUEST` | The envelope code is derived from the status |

Replaying the same 18 probes afterwards fails 13 architecture tests. **Lesson for the Evaluator:** test every hard gate against a set of known-bad probes, because "the gate passes" proves nothing on its own. Not fixed (learning project): the coverage gate still assigns layers by path.

## How each gate was proven to bite

Each gate was verified by adding a deliberate violation, confirming the expected failure, then deleting the probe:

- `throw new System.Exception(...)` in module code → build error RS0030.
- The same throw with the analyzer pragma-suppressed → `ApiCode_OnlyConstructsAppErrors` fails, including inside an async method.
- One probe set (routes using a repository and the bus, a service importing `NotificationService` and another module's repository, a repository using `HttpClient`, a write method on a contract, Staff importing Activities, Shared importing a module, a Reports subscriber using its repository) → 12 architecture tests failed, each naming the probe type.

## Candidate skill-file rules (from the corrections above)

- **architecture-principles**: "Only `*Service` classes may reference an `I*Repository`. Routes, `IEventSubscriber<T>` implementations and the auth handler delegate to their module's service." (B1, B2)
- **architecture-principles**: "Cross-module calls use only the target's `Contracts` query interface (`IStaffDirectory`, `IProgrammeQueries`, `IActivityQueries`). These interfaces have no write methods. A cross-module side effect is `IEventBus.PublishAsync(new XHappened(...))`. Importing `NotificationService` or `ReportService` is a hard-gate failure." (B3, B4)
- **coding-conventions**: "Throw only `ValidationError`, `UnauthorizedError`, `ForbiddenError`, `NotFoundError`, `ConflictError` or `BusinessRuleError`. Another store's resource is a `NotFoundError`, not a `ForbiddenError`." (A4, C4)
- **coding-conventions**: "Enums are PascalCase in C# and SCREAMING_SNAKE on the wire. Parse query enums with `StoreOpsJson.ParseQuery<T>`." (A3)
- **how-to-test**: "Every service test asserts the resulting state *and* the events published (`RecordingEventBus`). A routes test that asserts only a status code is incomplete. For a rejected request, also assert that nothing changed." (§2 failure mode 3)
- **how-to-review**: "`pwsh eng/verify.ps1` must print `VERIFY: PASS`. A coverage layer that shows 0/0 lines is a FAIL." (D1)
