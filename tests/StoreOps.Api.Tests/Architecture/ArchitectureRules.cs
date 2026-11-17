namespace StoreOps.Api.Tests.Architecture;

/// <summary>
/// What a module-internal type is, decided by name AND shape. A type that fits no role fails the build's
/// tests, so a rule can never be dodged by naming a class <c>ActivityEndpoints</c> or <c>ActivityStore</c>.
/// </summary>
internal enum Role
{
    Unclassified,
    Contract,
    Routes,
    Service,
    Repository,
    Subscriber,
    AuthenticationHandler,
    Registration,
    SeedData,
    Record,
}

/// <summary>Shared knowledge about the StoreOps module layout that the architecture tests check against.</summary>
internal static class ArchitectureRules
{
    public const string ApiRoot = "StoreOps.Api";
    public const string ModulesRoot = "StoreOps.Api.Modules";
    public const string SharedRoot = "StoreOps.Api.Shared";
    public const string EventBus = "StoreOps.Api.Shared.Events.IEventBus";
    public const string IntegrationEvent = "StoreOps.Api.Shared.Events.IIntegrationEvent";
    public const string EventSubscriber = "StoreOps.Api.Shared.Events.IEventSubscriber`1";
    public const string CurrentUser = "StoreOps.Api.Modules.Staff.Contracts.CurrentUser";
    public const string StoreOpsJson = "StoreOps.Api.Shared.Http.StoreOpsJson";

    /// <summary>
    /// The only cross-module dependencies allowed, always through the target's <c>.Contracts</c> namespace.
    /// Anything not listed is a violation. The list must stay acyclic (checked separately).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> AllowedModuleDependencies = new Dictionary<string, string[]>
    {
        ["Staff"] = [],
        ["Programmes"] = ["Staff"],
        ["Activities"] = ["Staff", "Programmes"],
        ["Alerts"] = ["Staff", "Activities"],
        ["Reports"] = ["Activities", "Programmes", "Staff"],
    };

    /// <summary>Modules other modules may reach only through their integration events (spec §3.5 "Event bus only").</summary>
    public static readonly string[] EventOnlyModules = ["Alerts", "Reports"];

    /// <summary>The only Shared interfaces module types may implement.</summary>
    public static readonly string[] ImplementableSharedInterfaces = [IntegrationEvent, EventSubscriber];

    /// <summary>Method-name prefixes allowed on cross-module contract interfaces: reads only.</summary>
    public static readonly string[] QueryPrefixes = ["Find", "Get", "List", "Exists", "Count"];

    /// <summary>Repository method-name prefixes that change state.</summary>
    public static readonly string[] WritePrefixes = ["Save", "Delete", "Add", "Update", "Remove", "Insert", "Upsert", "Set", "Clear"];

    /// <summary>The only Results/TypedResults factories routes may use; errors are AppErrors, never IResults.</summary>
    public static readonly string[] AllowedResultFactories = ["Ok", "Created", "CreatedAtRoute", "Accepted", "AcceptedAtRoute", "NoContent"];

    public static CodeModel Model => CodeModel.Api;

    public static IEnumerable<string> ModuleNames => AllowedModuleDependencies.Keys;

    public static IEnumerable<CodeType> ModuleTypes => Model.InNamespace(ModulesRoot);

    public static string Namespace(string module) => $"{ModulesRoot}.{module}";

    public static string ContractsNamespace(string module) => $"{ModulesRoot}.{module}.Contracts";

    /// <summary>The module a full type name belongs to, or <c>null</c> outside Modules.</summary>
    public static string? ModuleOf(string fullName)
        => CodeModel.IsInNamespace(fullName, ModulesRoot) ? fullName.Split('.')[3] : null;

    public static bool IsContract(string fullName)
        => ModuleOf(fullName) is { } module && CodeModel.IsInNamespace(fullName, ContractsNamespace(module));

    public static IEnumerable<CodeType> TypesOf(string module) => Model.InNamespace(Namespace(module));

    public static IEnumerable<CodeType> InternalTypesOf(string module) => TypesOf(module).Where(t => !IsContract(t.FullName));

    public static IEnumerable<CodeType> TypesWithRole(Role role) => ModuleTypes.Where(t => RoleOf(t) == role);

    public static bool DependsOn(string fromModule, string toModule)
        => TypesOf(fromModule).Any(t => t.References.Any(r => ModuleOf(r) == toModule));

    public static Role RoleOf(CodeType type)
    {
        if (IsContract(type.FullName))
        {
            return Role.Contract;
        }

        var d = type.Definition;
        return type.Name switch
        {
            _ when type.IsStaticClass && type.Name.EndsWith("Routes", StringComparison.Ordinal) => Role.Routes,
            _ when type.IsStaticClass && type.Name.EndsWith("Module", StringComparison.Ordinal) => Role.Registration,
            _ when type.IsStaticClass && type.Name.EndsWith("SeedData", StringComparison.Ordinal) => Role.SeedData,
            _ when d is { IsClass: true, IsSealed: true, IsAbstract: false } && type.Name.EndsWith("Service", StringComparison.Ordinal) && !type.IsRecord => Role.Service,
            _ when d.IsInterface && type.Name.StartsWith('I') && type.Name.EndsWith("Repository", StringComparison.Ordinal) => Role.Repository,
            _ when d is { IsClass: true, IsAbstract: false } && type.Name.EndsWith("Repository", StringComparison.Ordinal)
                && d.Interfaces.Any(i => i.InterfaceType.Name.EndsWith("Repository", StringComparison.Ordinal)) => Role.Repository,
            _ when type.Name.EndsWith("Subscriber", StringComparison.Ordinal) && type.Implements(EventSubscriber) => Role.Subscriber,
            _ when type.Name.EndsWith("AuthenticationHandler", StringComparison.Ordinal)
                && d.BaseType is Mono.Cecil.GenericInstanceType { ElementType.FullName: "Microsoft.AspNetCore.Authentication.AuthenticationHandler`1" } => Role.AuthenticationHandler,
            _ when type.IsRecord => Role.Record,
            _ => Role.Unclassified,
        };
    }

    /// <summary>Formats offending references as "Type → Referenced" lines for assertion messages.</summary>
    public static List<string> Offending(IEnumerable<CodeType> from, Func<CodeType, string, bool> isForbidden)
        => from.SelectMany(t => t.References.Where(r => isForbidden(t, r)).Select(r => $"{t.FullName} → {r}"))
            .Order(StringComparer.Ordinal)
            .ToList();

    public static string Lines(IEnumerable<string> items) => Environment.NewLine + string.Join(Environment.NewLine, items);
}
