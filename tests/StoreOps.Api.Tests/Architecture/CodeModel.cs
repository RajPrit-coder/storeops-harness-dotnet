using Mono.Cecil;
using Mono.Cecil.Cil;

namespace StoreOps.Api.Tests.Architecture;

/// <summary>
/// IL-level model of StoreOps.Api for the architecture rules. Every reference made anywhere inside a type,
/// including its lambdas, closures, async/iterator state machines and nested types, is charged to the
/// outermost declaring type. Name-based tools miss references made only inside an async lambda; this
/// model does not.
/// </summary>
internal sealed class CodeModel
{
    private CodeModel(ModuleDefinition module)
    {
        Module = module;
        Types = module.Types.Where(t => t.Name != "<Module>").Select(t => new CodeType(t)).ToList();
        ByName = Types.ToDictionary(t => t.FullName, StringComparer.Ordinal);
    }

    public static CodeModel Api { get; } = Load(typeof(Program).Assembly.Location);

    public ModuleDefinition Module { get; }

    public IReadOnlyList<CodeType> Types { get; }

    /// <summary>Gets the types written by developers (compiler-generated and embedded helper types excluded).</summary>
    public IEnumerable<CodeType> SourceTypes => Types.Where(t => !t.IsCompilerGenerated);

    public IReadOnlyDictionary<string, CodeType> ByName { get; }

    public static bool IsInNamespace(string fullName, string ns)
        => fullName.StartsWith(ns + ".", StringComparison.Ordinal);

    /// <summary>The outermost declaring type's full name, e.g. a closure inside ActivityRoutes → ActivityRoutes.</summary>
    public static string OuterName(TypeReference type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";
    }

    public static bool DerivesFrom(TypeReference type, string baseFullName)
    {
        for (var current = SafeResolve(type); current is not null; current = SafeResolve(current.BaseType))
        {
            if (current.FullName == baseFullName)
            {
                return true;
            }
        }

        return false;
    }

    public static TypeDefinition? SafeResolve(TypeReference? type)
    {
        if (type is null)
        {
            return null;
        }

        try
        {
            return type.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }

    public IEnumerable<CodeType> InNamespace(string ns) => SourceTypes.Where(t => t.IsIn(ns));

    private static CodeModel Load(string path)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(Microsoft.AspNetCore.Http.HttpContext).Assembly.Location)!);
        return new CodeModel(ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver }));
    }
}

/// <summary>A top-level type with everything it (and anything nested in it) references or calls.</summary>
internal sealed class CodeType
{
    private static readonly HashSet<string> CallOpCodes = ["call", "callvirt", "newobj", "ldftn", "ldvirtftn"];

    public CodeType(TypeDefinition definition)
    {
        Definition = definition;
        FullName = CodeModel.OuterName(definition);
        Namespace = definition.Namespace;
        Name = definition.Name;

        var references = new HashSet<string>(StringComparer.Ordinal);
        var calls = new List<CallSite>();
        Collect(definition, references, calls);
        references.Remove(FullName);
        References = references;
        Calls = calls;
    }

    public TypeDefinition Definition { get; }

    public string FullName { get; }

    public string Namespace { get; }

    public string Name { get; }

    /// <summary>Gets the outer full names of every type referenced, excluding this type itself.</summary>
    public IReadOnlySet<string> References { get; }

    /// <summary>Gets every call, constructor call and delegate creation made by this type's code.</summary>
    public IReadOnlyList<CallSite> Calls { get; }

    /// <summary>Gets a value indicating whether the type was emitted by a tool: the compiler, or coverlet's tracker under coverage runs.</summary>
    public bool IsCompilerGenerated => Name.StartsWith('<')
        || Namespace.StartsWith("Coverlet.", StringComparison.Ordinal)
        || Definition.CustomAttributes.Any(a => a.AttributeType.FullName is
            "System.Runtime.CompilerServices.CompilerGeneratedAttribute" or "Microsoft.CodeAnalysis.EmbeddedAttribute");

    public bool IsStaticClass => Definition is { IsClass: true, IsAbstract: true, IsSealed: true };

    public bool IsRecord => Definition.Methods.Any(m => m.Name == "<Clone>$");

    public bool IsIn(string ns) => Namespace == ns || CodeModel.IsInNamespace(FullName, ns);

    public bool Implements(string interfaceFullName)
        => Definition.Interfaces.Any(i => (i.InterfaceType is GenericInstanceType g ? g.ElementType.FullName : i.InterfaceType.FullName) == interfaceFullName);

    public override string ToString() => FullName;

    private static void Collect(TypeDefinition type, HashSet<string> refs, List<CallSite> calls)
    {
        Add(refs, type.BaseType);
        foreach (var i in type.Interfaces)
        {
            Add(refs, i.InterfaceType);
        }

        AddAttributes(refs, type.CustomAttributes);
        AddGenericConstraints(refs, type.GenericParameters);
        foreach (var field in type.Fields)
        {
            Add(refs, field.FieldType);
        }

        foreach (var property in type.Properties)
        {
            Add(refs, property.PropertyType);
        }

        foreach (var method in type.Methods)
        {
            CollectMethod(method, refs, calls);
        }

        foreach (var nested in type.NestedTypes)
        {
            Collect(nested, refs, calls);
        }
    }

    private static void CollectMethod(MethodDefinition method, HashSet<string> refs, List<CallSite> calls)
    {
        Add(refs, method.ReturnType);
        foreach (var parameter in method.Parameters)
        {
            Add(refs, parameter.ParameterType);
        }

        AddAttributes(refs, method.CustomAttributes);
        AddGenericConstraints(refs, method.GenericParameters);
        if (!method.HasBody)
        {
            return;
        }

        foreach (var variable in method.Body.Variables)
        {
            Add(refs, variable.VariableType);
        }

        foreach (var instruction in method.Body.Instructions)
        {
            switch (instruction.Operand)
            {
                case TypeReference typeRef:
                    Add(refs, typeRef);
                    break;
                case MethodReference methodRef:
                    AddMethod(refs, methodRef);
                    if (CallOpCodes.Contains(instruction.OpCode.Name))
                    {
                        calls.Add(new CallSite(method, instruction.OpCode, methodRef));
                    }

                    break;
                case FieldReference fieldRef:
                    Add(refs, fieldRef.DeclaringType);
                    Add(refs, fieldRef.FieldType);
                    break;
            }
        }
    }

    private static void AddMethod(HashSet<string> refs, MethodReference method)
    {
        Add(refs, method.DeclaringType);
        Add(refs, method.ReturnType);
        foreach (var parameter in method.Parameters)
        {
            Add(refs, parameter.ParameterType);
        }

        if (method is GenericInstanceMethod generic)
        {
            foreach (var argument in generic.GenericArguments)
            {
                Add(refs, argument);
            }
        }
    }

    private static void AddAttributes(HashSet<string> refs, IEnumerable<CustomAttribute> attributes)
    {
        foreach (var attribute in attributes)
        {
            Add(refs, attribute.AttributeType);
        }
    }

    private static void AddGenericConstraints(HashSet<string> refs, IEnumerable<GenericParameter> parameters)
    {
        foreach (var constraint in parameters.SelectMany(p => p.Constraints))
        {
            Add(refs, constraint.ConstraintType);
        }
    }

    private static void Add(HashSet<string> refs, TypeReference? type)
    {
        switch (type)
        {
            case null or GenericParameter:
                return;
            case GenericInstanceType generic:
                Add(refs, generic.ElementType);
                foreach (var argument in generic.GenericArguments)
                {
                    Add(refs, argument);
                }

                return;
            case IModifierType modified:
                Add(refs, modified.ModifierType);
                Add(refs, modified.ElementType);
                return;
            case TypeSpecification specification:
                Add(refs, specification.ElementType);
                return;
            default:
                refs.Add(CodeModel.OuterName(type));
                return;
        }
    }
}

internal sealed record CallSite(MethodDefinition Caller, OpCode OpCode, MethodReference Target)
{
    public string TargetType => CodeModel.OuterName(Target.DeclaringType);
}
