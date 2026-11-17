using StoreOps.Api.Shared.Events;

namespace StoreOps.Api.Modules.Programmes.Contracts;

// Public surface of the Programmes module (spec §3.3 Project / ProjectMember / ProjectRole,
// named Programme* here to match the module). Read lookups and integration events only.
public enum ProgrammeRole
{
    StoreManager,
    DepartmentLead,
    Associate,
}

/// <summary>Read-only programme lookups for other modules, implemented by ProgrammeService.</summary>
public interface IProgrammeQueries
{
    Task<ProgrammeDto?> FindByIdAsync(string programmeId, CancellationToken cancellationToken);
}

public sealed record ProgrammeDto(
    string Id,
    string StoreId,
    string Name,
    string? Description,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    string CreatedBy,
    IReadOnlyList<ProgrammeMemberDto> Members,
    DateTimeOffset CreatedAt);

public sealed record ProgrammeMemberDto(string UserId, ProgrammeRole Role, DateTimeOffset AddedAt);

public sealed record ProgrammeCreated(
    string ProgrammeId,
    string StoreId,
    string CreatedBy,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

public sealed record ProgrammeMemberAdded(
    string ProgrammeId,
    string StoreId,
    string UserId,
    ProgrammeRole Role,
    string AddedBy,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
