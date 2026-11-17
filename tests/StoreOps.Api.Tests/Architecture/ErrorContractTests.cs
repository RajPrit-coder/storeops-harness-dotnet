using StoreOps.Api.Shared.Errors;
using static StoreOps.Api.Tests.Architecture.ArchitectureRules;

namespace StoreOps.Api.Tests.Architecture;

/// <summary>Spec §3.5 "Error contract": no raw throws in services or routes; AppError hierarchy only.</summary>
public sealed class ErrorContractTests
{
    private static readonly string AppErrorName = typeof(AppError).FullName!;

    [Fact]
    public void EveryExceptionTypeInTheApi_DerivesFromAppError()
    {
        var offenders = Model.SourceTypes
            .Where(t => CodeModel.DerivesFrom(t.Definition, typeof(Exception).FullName!) && t.FullName != AppErrorName && !CodeModel.DerivesFrom(t.Definition, AppErrorName))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offenders.Count == 0, $"Exceptions must derive from AppError:{Lines(offenders)}");
    }

    [Fact]
    public void ApiCode_RaisesOnlyAppErrors()
    {
        // Backs up the banned-API analyzer (which can be suppressed or miss a type) by inspecting IL across
        // the WHOLE assembly, Program.cs included: no `new` of a non-AppError exception, and no static throw
        // helper on one (ObjectDisposedException.ThrowIf, ArgumentException.ThrowIfNullOrEmpty, ...).
        var offenders = Model.SourceTypes
            .SelectMany(t => t.Calls.Select(c => (Type: t, Call: c)))
            .Where(x => IsNonAppErrorException(x.Call.Target.DeclaringType)
                && (x.Call.OpCode.Name == "newobj" || !x.Call.Target.HasThis))
            .Select(x => $"{x.Type.FullName} uses {x.Call.Target.DeclaringType.FullName}.{x.Call.Target.Name}")
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0, $"Raise AppError subtypes only:{Lines(offenders)}");
    }

    private static bool IsNonAppErrorException(Mono.Cecil.TypeReference type)
        => CodeModel.DerivesFrom(type, typeof(Exception).FullName!) && !CodeModel.DerivesFrom(type, AppErrorName);
}
