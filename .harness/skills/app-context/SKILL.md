# Skill: app-context (StoreOps)

## Purpose
What StoreOps is and the facts every agent needs before planning, writing or reviewing a change.
Read by: Planner, Generator, Monitor.

## The application
StoreOps is a REST API for retail store operations: store teams run **programmes** (seasonal rollouts,
compliance drives), track **activities** (restocking runs, planogram resets, audits) across departments,
coordinate **staff**, receive **alerts**, and get **reports** by store and region.
Stack: .NET 10 / C# 12, ASP.NET Core minimal APIs, in-memory repositories (no database), xUnit +
WebApplicationFactory, StyleCop + nullable, warnings are errors.

## Layout
```
src/StoreOps.Api/
  Program.cs                       composition only (AddXModule / MapXModule)
  Shared/Errors|Events|Http        AppError hierarchy, IEventBus, StoreOpsJson (enum wire format)
  Modules/<Module>/                Activities, Programmes, Staff, Alerts, Reports
    Contracts/                     the ONLY public surface: query interface, DTOs, events
    <X>Routes.cs → <X>Service.cs → <X>Repository.cs, <X>Module.cs, records, *Subscriber.cs
tests/StoreOps.Api.Tests/
  Modules/<Module>/<X>ServiceTests.cs, <X>RoutesTests.cs, Shared/, Architecture/, Infrastructure/
eng/verify.ps1                     the one command that must pass
```

## Modules and their types (spec §3.3; Task*/Project* are named Activity*/Programme* in C#)
| Module | Types | Wire values (JSON + query strings, exact case) |
|---|---|---|
| activities | `Activity`, `ActivityStatus`, `ActivityPriority`, `ActivityCategory` | `TODO IN_PROGRESS DONE BLOCKED` · `LOW MEDIUM HIGH CRITICAL` · `RESTOCKING PLANOGRAM AUDIT COMPLIANCE GENERAL` |
| programmes | `Programme`, `ProgrammeMember`, `ProgrammeRole` | `STORE_MANAGER DEPARTMENT_LEAD ASSOCIATE` |
| staff | `User`, `UserProfile`, `StaffRole`, `AuthToken`, `CurrentUser` | `REGIONAL_MANAGER STORE_MANAGER DEPARTMENT_LEAD ASSOCIATE` |
| alerts | `Notification`, `NotificationChannel`, `NotificationStatus`, `AlertType` | `IN_APP EMAIL` · `UNREAD READ` · `INVENTORY SLA_BREACH SHIFT_HANDOVER ESCALATION` |
| reports | `Report`, `ReportType`, `ReportStatus` | `STORE_SUMMARY REGIONAL_ROLLUP DEPARTMENT_PERFORMANCE` · `PENDING READY FAILED` |

## Current API (spec §3.6 — all require a bearer token)
| # | Endpoint | Rules already implemented |
|---|---|---|
| 1 | `GET /api/activities?programmeId=&status=` | caller's store only |
| 2 | `POST /api/activities` | defaults TODO/MEDIUM/GENERAL; owner = caller; programme + assignee must be same store, assignee active |
| 3 | `GET /api/activities/{id}` | other store → 404 |
| 4 | `PATCH /api/activities/{id}` | status/priority/category/assigneeId; DONE cannot be reopened (422) |
| 5 | `DELETE /api/activities/{id}` | owner or STORE_MANAGER of that store, else 403 |
| 6 | `GET /api/programmes` | caller's store |
| 7 | `POST /api/programmes` | unique name per store (409) |
| 8 | `POST /api/programmes/{id}/members` | same store (422), active (422), not already member (409) |
| 9 | `GET /api/alerts` | notifications for the caller |

Existing events: `ActivityCreated`, `ActivityStatusChanged`, `ActivityAssigned`, `ActivityDeleted`
(Activities.Contracts), `ProgrammeCreated`, `ProgrammeMemberAdded` (Programmes.Contracts),
`NotificationCreated` (Alerts.Contracts). Alerts subscribes to `ActivityStatusChanged`: → BLOCKED raises an
`ESCALATION`/`IN_APP` notification to every active store manager of that store.

## Seed data (use these ids in acceptance criteria and tests)
| Token | User | Store | Role |
|---|---|---|---|
| `dev-token-store-manager` | `usr-sm-001` | store-001 | STORE_MANAGER |
| `dev-token-department-lead` | `usr-dl-001` | store-001 | DEPARTMENT_LEAD (GROCERY) |
| `dev-token-associate` | `usr-as-001` | store-001 | ASSOCIATE (GROCERY) |
| `dev-token-store-002-manager` | `usr-sm-002` | store-002 | STORE_MANAGER |
| `dev-token-regional-manager` | `usr-rm-001` | store-001 | REGIONAL_MANAGER |
`usr-as-002` (store-001) is inactive. Programmes: `prg-001` (store-001), `prg-002` (store-002).
Regions: store-001 and store-002 are in `region-north`. No activities are seeded — tests create their own.

## Commands
```
pwsh eng/verify.ps1                   build + format + all tests + coverage gate → must print VERIFY: PASS
dotnet test StoreOps.slnx -c Release --filter "FullyQualifiedName~AC1_2"     run one AC's tests
dotnet run --project src/StoreOps.Api                          http://localhost:5000 (Development: no token needed)
```
