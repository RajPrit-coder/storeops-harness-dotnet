using Mono.Cecil;
using static StoreOps.Api.Tests.Architecture.ArchitectureRules;

namespace StoreOps.Api.Tests.Architecture;

/// <summary>
/// Behavioural check behind "users is read-only" and "reports never writes": a method that implements a
/// cross-module contract must not write state or raise side effects, whatever it is called. A name check
/// alone would accept <c>IActivityQueries.FindAndMarkDoneAsync</c>.
/// </summary>
public sealed class ReadOnlyContractTests
{
    public static TheoryData<string> ContractImplementations()
    {
        var data = new TheoryData<string>();
        foreach (var (service, method) in ImplementingMethods())
        {
            data.Add($"{service.FullName}::{method.Name}");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ContractImplementations))]
    public void ContractImplementation_NeverWritesOrPublishes(string implementation)
    {
        var (service, method) = ImplementingMethods().Single(x => $"{x.Service.FullName}::{x.Method.Name}" == implementation);
        var repositories = TypesWithRole(Role.Repository).Select(t => t.FullName).ToHashSet();

        var writes = Reachable(service, method)
            .SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand as MethodReference)
            .Where(target => target is not null)
            .Where(target =>
                (repositories.Contains(CodeModel.OuterName(target!.DeclaringType)) && WritePrefixes.Any(p => target.Name.StartsWith(p, StringComparison.Ordinal)))
                || CodeModel.OuterName(target.DeclaringType) == EventBus)
            .Select(target => $"{target!.DeclaringType.Name}.{target.Name}")
            .Distinct()
            .ToList();

        Assert.True(writes.Count == 0, $"{implementation} implements a read-only contract but calls:{Lines(writes)}");
    }

    [Fact]
    public void EveryContractInterfaceMethod_HasAnImplementationUnderTest()
    {
        var implemented = ImplementingMethods().Select(x => x.Method.Name).ToHashSet(StringComparer.Ordinal);
        var declared = ModuleTypes
            .Where(t => IsContract(t.FullName) && t.Definition.IsInterface)
            .SelectMany(t => t.Definition.Methods.Select(m => m.Name));

        Assert.All(declared, name => Assert.Contains(name, implemented));
    }

    private static IEnumerable<(CodeType Service, MethodDefinition Method)> ImplementingMethods()
    {
        foreach (var type in ModuleTypes.Where(t => !IsContract(t.FullName) && !t.Definition.IsInterface))
        {
            var contracts = type.Definition.Interfaces
                .Select(i => CodeModel.SafeResolve(i.InterfaceType))
                .Where(i => i is not null && IsContract(CodeModel.OuterName(i)));

            foreach (var contract in contracts)
            {
                foreach (var declared in contract!.Methods)
                {
                    var implementation = type.Definition.Methods.FirstOrDefault(m =>
                        m.Overrides.Any(o => o.Name == declared.Name && o.DeclaringType.FullName == contract.FullName)
                        || (m.Name == declared.Name && m.Parameters.Count == declared.Parameters.Count && m.IsPublic && !m.IsStatic));
                    if (implementation is not null)
                    {
                        yield return (type, implementation);
                    }
                }
            }
        }
    }

    /// <summary>Methods reachable from <paramref name="start"/> inside the same type: helpers, async state machines, lambdas.</summary>
    private static List<MethodDefinition> Reachable(CodeType owner, MethodDefinition start)
    {
        var seen = new HashSet<MethodDefinition>();
        var queue = new Queue<MethodDefinition>([start]);
        while (queue.TryDequeue(out var method))
        {
            if (!seen.Add(method))
            {
                continue;
            }

            var stateMachine = method.CustomAttributes
                .Where(a => a.AttributeType.Name is "AsyncStateMachineAttribute" or "IteratorStateMachineAttribute")
                .Select(a => CodeModel.SafeResolve(a.ConstructorArguments[0].Value as TypeReference))
                .FirstOrDefault();
            foreach (var moveNext in stateMachine?.Methods.Where(m => m.Name == "MoveNext") ?? [])
            {
                queue.Enqueue(moveNext);
            }

            if (!method.HasBody)
            {
                continue;
            }

            foreach (var target in method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>())
            {
                if (CodeModel.OuterName(target.DeclaringType) == owner.FullName && SafeResolve(target) is { } local)
                {
                    queue.Enqueue(local);
                }
            }
        }

        return seen.Where(m => m.HasBody).ToList();
    }

    private static MethodDefinition? SafeResolve(MethodReference method)
    {
        try
        {
            return method.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }
}
