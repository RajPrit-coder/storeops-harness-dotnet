using static StoreOps.Api.Tests.Architecture.ArchitectureRules;

namespace StoreOps.Api.Tests.Architecture;

/// <summary>Spec §3.5 "Layer separation": Routes → Service → Repository, no skipping.</summary>
public sealed class LayeringTests
{
    public static TheoryData<string> AllModules() => new(ModuleNames);

    [Fact]
    public void EveryModuleType_HasAKnownRole()
    {
        // Roles are decided by name AND shape; anything else (ActivityEndpoints, ActivityStore, a non-sealed
        // service, ...) must be renamed or reshaped so every layer rule below applies to it.
        var unclassified = ModuleTypes.Where(t => RoleOf(t) == Role.Unclassified).Select(t => t.FullName).ToList();

        Assert.True(unclassified.Count == 0, $"Module types with no recognised layer role:{Lines(unclassified)}");
    }

    [Theory]
    [MemberData(nameof(AllModules))]
    public void Module_HasRoutesServiceAndRepositoryLayers(string module)
    {
        var roles = TypesOf(module).Select(RoleOf).ToHashSet();

        Assert.Contains(Role.Routes, roles);
        Assert.Contains(Role.Service, roles);
        Assert.Contains(Role.Repository, roles);
    }

    [Fact]
    public void OnlyServices_UseRepositories()
    {
        // Routes, event subscribers and the auth handler are inbound adapters: they go through the service.
        var repositories = TypesWithRole(Role.Repository).Select(t => t.FullName).ToHashSet();

        var offending = Offending(
            ModuleTypes.Where(t => RoleOf(t) is not (Role.Service or Role.Repository or Role.Registration)),
            (_, r) => repositories.Contains(r));

        Assert.True(offending.Count == 0, $"Only services may use repositories:{Lines(offending)}");
    }

    [Fact]
    public void OnlyRoutes_MapEndpoints()
    {
        var offending = Model.SourceTypes
            .Where(t => !t.IsIn(ModulesRoot) || RoleOf(t) != Role.Routes)
            .SelectMany(t => t.Calls
                .Where(c => c.Target.Name.StartsWith("Map", StringComparison.Ordinal) && c.Target.DeclaringType.Namespace == "Microsoft.AspNetCore.Builder")
                .Select(c => $"{t.FullName} calls {c.Target.DeclaringType.Name}.{c.Target.Name}"))
            .Distinct()
            .ToList();

        Assert.True(offending.Count == 0, $"Endpoints may only be mapped in a module's *Routes class:{Lines(offending)}");
    }

    [Fact]
    public void Routes_UseOnlyTheirOwnServiceAndHttpConcerns()
    {
        // §3.5 "Routes must not contain business logic": a route binds input, calls its own module's service
        // and shapes the response. Query contracts, repositories, the event bus or other modules' services
        // in a route mean business logic has leaked into the HTTP layer.
        var offending = Offending(TypesWithRole(Role.Routes), (route, r) => !IsAllowedForRoutes(route, r));

        Assert.True(offending.Count == 0, $"Routes may only use their own service, request records, DTOs, CurrentUser and StoreOpsJson:{Lines(offending)}");
    }

    [Fact]
    public void ModuleCode_ProducesErrorsOnlyAsAppErrors()
    {
        // Results.Problem / BadRequest(obj) / NotFound(...) etc. bypass the AppError envelope.
        var offending = ModuleTypes
            .SelectMany(t => t.Calls
                .Where(c => c.TargetType is "Microsoft.AspNetCore.Http.Results" or "Microsoft.AspNetCore.Http.TypedResults"
                    && !AllowedResultFactories.Contains(c.Target.Name))
                .Select(c => $"{t.FullName} calls {c.Target.DeclaringType.Name}.{c.Target.Name}"))
            .Concat(Offending(ModuleTypes, (_, r) => r.Contains("ProblemDetails", StringComparison.Ordinal)))
            .Distinct()
            .ToList();

        Assert.True(offending.Count == 0, $"Error responses must come from AppErrors:{Lines(offending)}");
    }

    [Fact]
    public void Subscribers_DelegateToTheirOwnService()
    {
        var offending = Offending(
            TypesWithRole(Role.Subscriber),
            (subscriber, r) => r.StartsWith(ApiRoot + ".", StringComparison.Ordinal)
                && !(IsContract(r) && Model.ByName.TryGetValue(r, out var evt) && evt.Implements(IntegrationEvent))
                && !(IsContract(r) && ModuleOf(r) == ModuleOf(subscriber.FullName))
                && !(ModuleOf(r) == ModuleOf(subscriber.FullName) && Model.ByName.TryGetValue(r, out var own) && RoleOf(own) == Role.Service)
                && r != EventSubscriber);

        Assert.True(offending.Count == 0, $"Subscribers may only use events and their own module's service:{Lines(offending)}");
    }

    [Fact]
    public void Services_DoNotDependOnRoutes()
    {
        var routes = TypesWithRole(Role.Routes).Select(t => t.FullName).ToHashSet();

        var offending = Offending(TypesWithRole(Role.Service), (_, r) => routes.Contains(r));

        Assert.True(offending.Count == 0, $"Services must not depend on routes:{Lines(offending)}");
    }

    [Theory]
    [MemberData(nameof(AllModules))]
    public void Repositories_AreDataAccessOnly(string module)
    {
        // "Repositories must not call external services": no HTTP, no outbound calls, no events,
        // no other modules, no upward references to their own service or routes.
        var offending = Offending(
            TypesOf(module).Where(t => RoleOf(t) == Role.Repository),
            (_, r) => r.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)
                || r.StartsWith("System.Net.Http.", StringComparison.Ordinal)
                || r == EventBus
                || (ModuleOf(r) is { } other && other != module)
                || (Model.ByName.TryGetValue(r, out var t) && RoleOf(t) is Role.Service or Role.Routes or Role.Subscriber));

        Assert.True(offending.Count == 0, $"{module} repositories must only do data access:{Lines(offending)}");
    }

    private static bool IsAllowedForRoutes(CodeType route, string reference)
    {
        if (!reference.StartsWith(ApiRoot + ".", StringComparison.Ordinal))
        {
            return true; // framework and BCL types; result factories are checked separately
        }

        if (reference is CurrentUser or StoreOpsJson)
        {
            return true;
        }

        if (ModuleOf(reference) != ModuleOf(route.FullName) || !Model.ByName.TryGetValue(reference, out var target))
        {
            return false;
        }

        return RoleOf(target) is Role.Service or Role.Record
            || (RoleOf(target) == Role.Contract && !target.Definition.IsInterface);
    }
}
