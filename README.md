# StoreOps API (.NET)

The StoreOps reference codebase for the Build Track capstone. It is a REST API for retail store operations: an ASP.NET Core minimal-API modular monolith with five modules, in-memory storage and stub business logic. It implements the base surface in spec §3.6 and enforces the architecture rules in §3.5 with automated checks.

## Quick start

```bash
pwsh eng/verify.ps1                       # build + format + tests + per-layer coverage gate
dotnet build && dotnet test               # the spec's stack check (§3.2, Option D)
dotnet run --project src/StoreOps.Api     # http://localhost:5000
```

Every endpoint requires a bearer token. **In Development only**, a request with no `Authorization` header is signed in as the store manager (`Staff:DevAutoLoginToken` in `appsettings.Development.json`), so URLs work straight from a browser. A wrong token still gets 401. Ready-made requests are in `src/StoreOps.Api/StoreOps.Api.http`. The seeded development tokens are listed below and defined in `Modules/Staff/StaffSeedData.cs`:

```bash
curl -H "Authorization: Bearer dev-token-associate" http://localhost:5000/api/activities
```

| Token | User | Store | Role |
|---|---|---|---|
| `dev-token-store-manager` | `usr-sm-001` | store-001 | STORE_MANAGER |
| `dev-token-department-lead` | `usr-dl-001` | store-001 | DEPARTMENT_LEAD (GROCERY) |
| `dev-token-associate` | `usr-as-001` | store-001 | ASSOCIATE (GROCERY) |
| `dev-token-store-002-manager` | `usr-sm-002` | store-002 | STORE_MANAGER |
| `dev-token-regional-manager` | `usr-rm-001` | store-001 | REGIONAL_MANAGER |

`usr-as-002` is an inactive associate, so its token is rejected. `dev-token-expired` is also rejected. Seeded programmes are `prg-001` (store-001) and `prg-002` (store-002).

## Layout (spec §7, adapted to .NET)

```
src/StoreOps.Api/
  Program.cs                      composition root
  Shared/                         spec "shared/": Errors (AppError + envelope), Events (bus), Http (JSON enums)
  Modules/<Module>/
    Contracts/                    the module's ONLY public surface: read-only query interface, DTOs, events
    <X>Routes.cs                  HTTP + input validation only            ─┐
    <X>Service.cs                 business logic; implements the query  ├─ Routes → Service → Repository
    <X>Repository.cs              I<X>Repository + in-memory data access ─┘
    *Subscribers.cs / *Handler    inbound adapters (event bus, auth): delegate to the Service
    <X>Module.cs                  DI registration + route mapping
tests/StoreOps.Api.Tests/         mirrors src: Modules/<Module>/, Shared/, plus Architecture/
eng/verify.ps1, eng/check-coverage.ps1
```

## Domain types (spec §3.3)

| Module | C# types | Wire values |
|---|---|---|
| activities | `Activity`, `ActivityStatus`, `ActivityPriority`, `ActivityCategory` (the spec's Task*, renamed because `Task` and `TaskStatus` collide with `System.Threading.Tasks`) | `TODO` `IN_PROGRESS` `DONE` `BLOCKED` · `LOW` `MEDIUM` `HIGH` `CRITICAL` · `RESTOCKING` `PLANOGRAM` `AUDIT` `COMPLIANCE` `GENERAL` |
| programmes | `Programme`, `ProgrammeMember`, `ProgrammeRole` (the spec's Project*, renamed to match the module) | `STORE_MANAGER` `DEPARTMENT_LEAD` `ASSOCIATE` |
| staff | `User`, `UserProfile`, `StaffRole`, `AuthToken` | `REGIONAL_MANAGER` `STORE_MANAGER` `DEPARTMENT_LEAD` `ASSOCIATE` |
| alerts | `Notification`, `NotificationChannel`, `NotificationStatus`, `AlertType` | `IN_APP` `EMAIL` · `UNREAD` `READ` · `INVENTORY` `SLA_BREACH` `SHIFT_HANDOVER` `ESCALATION` |
| reports | `Report`, `ReportType`, `ReportStatus` | `STORE_SUMMARY` `REGIONAL_ROLLUP` `DEPARTMENT_PERFORMANCE` · `PENDING` `READY` `FAILED` |

Enums are PascalCase in C# and SCREAMING_SNAKE on the wire, in both JSON bodies and query strings (`Shared/Http/StoreOpsJson.cs`).

## Endpoints (spec §3.6)

| # | Method & path | Behaviour |
|---|---|---|
| 1 | `GET /api/activities?programmeId=&status=` | Activities of the caller's store, with optional filters |
| 2 | `POST /api/activities` | Creates an activity: status `TODO`, priority `MEDIUM` and category `GENERAL` by default, owned by the caller. Validates the programme (same store) and the assignee (same store, active). Emits `ActivityCreated` and, when an assignee is set, `ActivityAssigned` |
| 3 | `GET /api/activities/{id}` | Returns 404 for an unknown id or an activity in another store |
| 4 | `PATCH /api/activities/{id}` | Updates `status`, `priority`, `category` or `assigneeId`. A `DONE` activity cannot be reopened (422). Emits `ActivityStatusChanged` and `ActivityAssigned` |
| 5 | `DELETE /api/activities/{id}` | Only the owner or a store manager of that store can delete (otherwise 403). Emits `ActivityDeleted` |
| 6 | `GET /api/programmes` | Programmes of the authenticated store |
| 7 | `POST /api/programmes` | Creates a programme. The name must be unique within the store (409) and `endsOn` must not be before `startsOn` |
| 8 | `POST /api/programmes/{id}/members` | Body `{ userId, role }`. The staff member must be in the same store (422) and active (422), and not already a member (409) |
| 9 | `GET /api/alerts` | Notifications for the authenticated user |

Staff (authentication only) and reports (a §3.4 demonstration feature) have no endpoints yet. Their `*Routes.cs` files exist and map nothing, so new endpoints have a home that follows the three-layer layout.

The one base side effect is this: when an activity becomes `BLOCKED`, Alerts receives `ActivityStatusChanged` from the event bus and creates an `ESCALATION` / `IN_APP` notification for each active store manager of that store.

## Error contract

Every error, whatever produced it, uses one envelope: an `AppError`, a model-binding failure, an auth challenge, an unknown route or an unsupported method.

```json
{ "error": { "code": "VALIDATION_FAILED", "message": "Activity title is required.", "statusCode": 400, "field": "title" } }
```

| Source | Code | Status |
|---|---|---|
| `ValidationError`, or an enum value not in the spec's exact wire format (e.g. `InProgress`, `high`) | `VALIDATION_FAILED` | 400 |
| malformed JSON | `BAD_REQUEST` | 400 |
| non-JSON body | `UNSUPPORTED_MEDIA_TYPE` | 415 |
| missing, invalid or expired token (`UnauthorizedError`) | `UNAUTHORIZED` | 401 |
| `ForbiddenError` | `FORBIDDEN` | 403 |
| `NotFoundError`, unknown route | `NOT_FOUND` | 404 |
| unsupported method | `METHOD_NOT_ALLOWED` | 405 |
| `ConflictError` | `CONFLICT` | 409 |
| `BusinessRuleError` | `BUSINESS_RULE_VIOLATION` | 422 |
| anything else | `INTERNAL_ERROR` | 500 (logged; details are never returned) |

## Spec §3.5 rules and how they are enforced

| Rule | Automated check (all fail the build or `dotnet test`) |
|---|---|
| **Module boundary**: no import of another module's repository; cross-module reads go through the target's service | `ModuleBoundaryTests.Module_NeverImportsAnotherModulesRepository`, `Module_ReachesOtherModules_OnlyThroughTheirContracts`. Cross-module reads use the `Contracts` query interface (`IStaffDirectory`, `IProgrammeQueries`, `IActivityQueries`), which each module's Service implements |
| **No circular imports** | `Module_DependsOnlyOnDeclaredModules` (allow-list in `ArchitectureRules.AllowedModuleDependencies`) and `ModuleDependencyGraph_HasNoCycles` |
| **Event bus only**: no direct `NotificationService` / `ReportService` imports | `EventOnlyModule_IsReachedByOthers_OnlyThroughItsEvents` (Alerts, Reports) and `ModuleContracts_ExposeReadOnlyLookupsAndImmutableData` (no cross-module write methods exist) |
| **Error contract**: no raw throws | Build: `BannedSymbols.txt` (RS0030 is an error). Tests: `ErrorContractTests` (every exception type derives from `AppError`; an IL scan finds no `new` of a non-`AppError` exception, including in async state machines) |
| **Layer separation** | `LayeringTests`: only Services use repositories (routes, subscribers and the auth handler can't); routes publish no events; services don't reference routes; repositories use no HTTP, `HttpClient`, event bus or other modules |
| **Read-only reports** | Contracts are query-only (above), and `ReportServiceTests.GenerateStoreSummary_IsReadOnlyTowardsOtherModules` checks this |
| **Users (staff) read-only to other modules** | Same contract rule: `IStaffDirectory` has `Find*` and `List*` methods only, and its DTOs are immutable |
| **Coverage** (§3.6): Service 80%, Routes 70%, Shared 60%, overall 70% | `eng/check-coverage.ps1`, run by `eng/verify.ps1`. A layer that matches no files fails, so the gate can't pass on an empty match |

The architecture tests run on an IL model of the assembly (`tests/.../Architecture/CodeModel.cs`, Mono.Cecil). It charges every reference inside lambdas, closures and async state machines to the outer type, and it covers `Program.cs` and any folder, not just `Modules/`. Each check was verified to fail on a deliberate violation. See [docs/bootstrap-corrections.md](docs/bootstrap-corrections.md).

## Lint and type checking

`Directory.Build.props` sets nullable on, `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`, StyleCop.Analyzers and C# 12. The `.editorconfig` file records each StyleCop rule that is switched off and why. `dotnet format --verify-no-changes` is the formatting gate. Package versions are managed centrally in `Directory.Packages.props`, and `nuget.config` points restore at nuget.org.

## Known gaps (stubs, by design)

- `PATCH` cannot unassign an activity (a null `assigneeId` means "no change").
- Updates do read-then-write with no concurrency control. That is fine for the in-memory stub and a real issue once there is a database.
- No `Dockerfile` yet. It belongs to the deployment step (spec §3.4).
- The coverage gate sorts files into layers by path pattern, so a service in a subfolder or a partial file falls under the overall threshold only.
- The event bus has no outbox, and reports never reach `FAILED`. Neither matters for an in-memory learning stub.
