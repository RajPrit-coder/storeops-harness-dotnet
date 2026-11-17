using System.Runtime.CompilerServices;
using static StoreOps.Api.Tests.Architecture.ArchitectureRules;

namespace StoreOps.Api.Tests.Architecture;

/// <summary>Spec §3.5 "Module boundary", "Event bus only" and "Read-only reports", plus no circular imports.</summary>
public sealed class ModuleBoundaryTests
{
    public static TheoryData<string> AllModules() => new(ModuleNames);

    public static TheoryData<string> EventOnlyTargets() => new(EventOnlyModules);

    [Fact]
    public void EveryModuleNamespace_IsCoveredByTheRules()
    {
        var discovered = ModuleTypes.Select(t => ModuleOf(t.FullName)!).Distinct().Order(StringComparer.Ordinal);

        Assert.Equal(ModuleNames.Order(StringComparer.Ordinal), discovered);
    }

    [Fact]
    public void EveryType_LivesInAModuleOrShared_OrIsProgram()
    {
        // Nothing may sit outside the governed namespaces (e.g. a "Features" folder or extra top-level code).
        var strays = Model.SourceTypes
            .Where(t => !t.IsIn(ModulesRoot) && !t.IsIn(SharedRoot) && t.FullName != "Program")
            .Select(t => t.FullName)
            .ToList();

        Assert.True(strays.Count == 0, $"Types outside Modules/Shared:{Lines(strays)}");
    }

    [Fact]
    public void Program_OnlyComposesModuleRegistrations()
    {
        var offending = Offending(
            [Model.ByName["Program"]],
            (_, r) => ModuleOf(r) is not null && (!Model.ByName.TryGetValue(r, out var t) || RoleOf(t) != Role.Registration));

        Assert.True(offending.Count == 0, $"Program.cs may only call *Module registrations:{Lines(offending)}");
    }

    [Theory]
    [MemberData(nameof(AllModules))]
    public void Module_NeverImportsAnotherModulesRepository(string module)
    {
        // §3.5 Module boundary: "No module may import directly from another module's repository."
        var foreignRepositories = TypesWithRole(Role.Repository).Where(t => ModuleOf(t.FullName) != module).Select(t => t.FullName).ToHashSet();

        var offending = Offending(TypesOf(module), (_, r) => foreignRepositories.Contains(r));

        Assert.True(offending.Count == 0, $"{module} imports another module's repository:{Lines(offending)}");
    }

    [Theory]
    [MemberData(nameof(AllModules))]
    public void Module_ReachesOtherModules_OnlyThroughTheirContracts(string module)
    {
        var offending = Offending(TypesOf(module), (_, r) => ModuleOf(r) is { } other && other != module && !IsContract(r));

        Assert.True(offending.Count == 0, $"{module} reaches into another module's internals:{Lines(offending)}");
    }

    [Theory]
    [MemberData(nameof(AllModules))]
    public void Module_DependsOnlyOnDeclaredModules(string module)
    {
        var undeclared = ModuleNames
            .Where(other => other != module && DependsOn(module, other))
            .Except(AllowedModuleDependencies[module])
            .ToList();

        Assert.True(undeclared.Count == 0, $"{module} has undeclared dependencies on: {string.Join(", ", undeclared)}");
    }

    [Fact]
    public void ModuleDependencyGraph_HasNoCycles()
    {
        var graph = ModuleNames.ToDictionary(m => m, m => ModuleNames.Where(o => o != m && DependsOn(m, o)).ToList());

        var done = new HashSet<string>();
        foreach (var module in ModuleNames)
        {
            var cycle = FindCycle(module, graph, [], done);
            Assert.True(cycle is null, $"Circular module dependency: {string.Join(" -> ", cycle ?? [])}");
        }
    }

    [Theory]
    [MemberData(nameof(EventOnlyTargets))]
    public void EventOnlyModule_IsReachedByOthers_OnlyThroughItsEvents(string target)
    {
        // §3.5 Event bus only: "no direct NotificationService or ReportService imports". Others may
        // consume the target's integration events (and the enums they carry), nothing else.
        var allowed = TypesOf(target)
            .Where(t => t.Definition.IsEnum || t.Implements(IntegrationEvent))
            .Select(t => t.FullName)
            .ToHashSet();

        var offending = Offending(ModuleTypes.Where(t => ModuleOf(t.FullName) != target), (_, r) => ModuleOf(r) == target && !allowed.Contains(r));

        Assert.True(offending.Count == 0, $"Modules must trigger {target} via the event bus:{Lines(offending)}");
    }

    [Fact]
    public void Reports_IsALeaf_NothingDependsOnIt()
    {
        // §3.5 Read-only reports: if no module consumes anything from Reports, nothing Reports does can
        // cause a write elsewhere. Its own reads go through query-only contracts (checked below).
        var offending = Offending(ModuleTypes.Where(t => ModuleOf(t.FullName) != "Reports"), (_, r) => ModuleOf(r) == "Reports");

        Assert.True(offending.Count == 0, $"No module may depend on Reports:{Lines(offending)}");
    }

    [Fact]
    public void IntegrationEvents_AreDeclaredInThePublishersContracts()
    {
        // An event declared in Shared hides the publisher→subscriber dependency from every module rule.
        var misplaced = Model.SourceTypes
            .Where(t => !t.Definition.IsInterface && t.Implements(IntegrationEvent) && !IsContract(t.FullName))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(misplaced.Count == 0, $"Integration events must live in a module's Contracts namespace:{Lines(misplaced)}");
    }

    [Fact]
    public void ModuleTypes_ImplementOnlyTheAllowedSharedInterfaces()
    {
        // A Shared interface implemented in one module and injected into another is a hidden cross-module port.
        var offending = ModuleTypes
            .SelectMany(t => t.Definition.Interfaces.Select(i => (Type: t, Interface: CodeModel.OuterName(i.InterfaceType))))
            .Where(x => x.Interface.StartsWith(SharedRoot + ".", StringComparison.Ordinal) && !ImplementableSharedInterfaces.Contains(x.Interface))
            .Select(x => $"{x.Type.FullName} implements {x.Interface}")
            .ToList();

        Assert.True(offending.Count == 0, $"Module types may only implement IIntegrationEvent / IEventSubscriber<T> from Shared:{Lines(offending)}");
    }

    [Theory]
    [MemberData(nameof(AllModules))]
    public void ModuleContracts_ExposeReadOnlyLookupsAndImmutableData(string module)
    {
        var contracts = TypesOf(module).Where(t => IsContract(t.FullName)).ToList();

        var mutatingMethods = contracts
            .Where(t => t.Definition.IsInterface)
            .SelectMany(t => t.Definition.Methods.Select(m => $"{t.Name}.{m.Name}"))
            .Where(name => !QueryPrefixes.Any(p => name.Split('.')[1].StartsWith(p, StringComparison.Ordinal)))
            .ToList();

        var settable = contracts
            .Where(t => t.Definition is { IsInterface: false, IsEnum: false })
            .SelectMany(t => t.Definition.Properties)
            .Where(p => p.SetMethod is { IsPublic: true } setter
                && !setter.ReturnType.FullName.Contains(nameof(IsExternalInit), StringComparison.Ordinal))
            .Select(p => $"{p.DeclaringType.Name}.{p.Name}")
            .ToList();

        Assert.True(mutatingMethods.Count == 0, $"{module} contracts expose mutations:{Lines(mutatingMethods)}");
        Assert.True(settable.Count == 0, $"{module} contract types are mutable:{Lines(settable)}");
    }

    [Theory]
    [MemberData(nameof(AllModules))]
    public void ModuleContracts_DoNotLeakModuleInternals(string module)
    {
        var offending = Offending(TypesOf(module).Where(t => IsContract(t.FullName)), (_, r) => ModuleOf(r) == module && !IsContract(r));

        Assert.True(offending.Count == 0, $"{module}.Contracts exposes internal types:{Lines(offending)}");
    }

    [Fact]
    public void Shared_DoesNotDependOnAnyModule()
    {
        var offending = Offending(Model.InNamespace(SharedRoot), (_, r) => ModuleOf(r) is not null);

        Assert.True(offending.Count == 0, $"Shared must not depend on modules:{Lines(offending)}");
    }

    private static List<string>? FindCycle(string node, Dictionary<string, List<string>> graph, List<string> path, HashSet<string> done)
    {
        if (path.Contains(node))
        {
            return [.. path, node];
        }

        if (done.Contains(node))
        {
            return null;
        }

        foreach (var next in graph[node])
        {
            if (FindCycle(next, graph, [.. path, node], done) is { } cycle)
            {
                return cycle;
            }
        }

        done.Add(node);
        return null;
    }
}
