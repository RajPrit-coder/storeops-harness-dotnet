# Skill: coding-conventions (StoreOps / .NET)

## Purpose
How StoreOps code is written so it compiles warning-free, passes the architecture tests first time and
reads like the rest of the codebase. Read by: Generator.

## Placement — where each piece goes
| You need | Put it in | Shape |
|---|---|---|
| Endpoint | `Modules/<M>/<X>Routes.cs` | `internal static class`; `group.MapPatch("/bulk-status", HandlerAsync)`; private static handler methods |
| Request body | `Modules/<M>/<Entity>.cs` (next to the entity) | `internal sealed record XRequest(...)` with **nullable** fields — the service validates |
| Business logic | `Modules/<M>/<X>Service.cs` | `internal sealed class XService(deps…)` primary constructor |
| Data access | `Modules/<M>/<X>Repository.cs` | `internal interface IXRepository` + `internal sealed class InMemoryXRepository` (ConcurrentDictionary) |
| Response DTO / enum / event | `Modules/<M>/Contracts/<M>Contracts.cs` | `public sealed record`; events implement `IIntegrationEvent` |
| Reaction to another module's event | `Modules/<consumer>/<Topic>Subscribers.cs` | `internal sealed class XSubscriber : IEventSubscriber<TEvent>` → one-line call to own service |
| DI registration | `Modules/<M>/<M>Module.cs` | `services.AddScoped<XService>()`, `AddSingleton<IXRepository, InMemoryXRepository>()` |

- **No sub-folders inside a module** (the coverage gate assigns layers by path).
- New entity or audit record → a `record` in the module root, e.g. `internal sealed record ActivityAuditEntry(...)`.

## Naming (decides the role the architecture tests give a type)
`*Routes` static class · `*Service` sealed class · `I*Repository` interface / `InMemory*Repository` ·
`*Module` static class · `*Subscriber` (implements `IEventSubscriber<T>`) · `*SeedData` static class ·
everything else must be a `record`. A class called `ActivityEndpoints`, `BulkHelper` or `ActivityStore`
fails `EveryModuleType_HasAKnownRole`.

## Patterns to copy (see `ActivityService`, `ActivityRoutes`)
- **Caller:** route handlers take `CurrentUser user`; pass it to the service. Never read claims yourself.
- **Store scoping:** load via the existing `GetVisibleAsync(user, id, ct)` pattern — other store → `NotFoundError`.
- **Permissions:** `user.IsStoreManagerOf(storeId)`; violation → `ForbiddenError("…")`.
- **Time / ids:** `timeProvider.GetUtcNow()`, `Ids.New("act")`. Never `DateTime.Now`, never `Guid.NewGuid()` directly.
- **Enums on the wire:** handled by `StoreOpsJson` — body values must be exact (`"DONE"`). For query
  strings use `StoreOpsJson.ParseQuery<ActivityStatus>(raw, "status")`.
- **Events:** save first, then `await eventBus.PublishAsync(new ActivityStatusChanged(…), ct)` — one event
  per actual state change, none when nothing changed.
- **Responses:** `TypedResults.Ok(dto)`, `TypedResults.Created(location, dto)`, `TypedResults.NoContent()`.
  Partial success is a **200 with a per-item result list**, not an error.

## Errors — which one to throw
| Situation | Throw |
|---|---|
| Missing/invalid field value | `new ValidationError("…", "fieldName")` |
| Unknown id, or another store's resource | `new NotFoundError("Activity", id)` |
| Authenticated but not allowed | `new ForbiddenError("…")` |
| Duplicate / already in that state | `new ConflictError("…")` |
| Business rule (e.g. DONE cannot be reopened) | `new BusinessRuleError("…")` |
Per-item failures inside a bulk request are **reported in the response**, not thrown, using the same codes.

## Compiler / StyleCop rules that bite (warnings are errors)
- File-scoped `namespace X;`, usings outside the namespace, `System` usings first, no unused usings.
- Private fields `_camelCase`; static readonly / const `PascalCase`; braces on every `if`.
- Member order: static members before instance members; properties before methods; public before private.
- C# 12 only (no `field` keyword, no C# 13 features). Nullable is on — no `!` to silence a real null.
- No new NuGet packages.
- Run `dotnet format StoreOps.slnx` if `eng/verify.ps1` fails at the `format` step.
