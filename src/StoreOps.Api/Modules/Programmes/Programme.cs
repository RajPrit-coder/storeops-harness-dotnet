using StoreOps.Api.Modules.Programmes.Contracts;

namespace StoreOps.Api.Modules.Programmes;

internal sealed record Programme(
    string Id,
    string StoreId,
    string Name,
    string? Description,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    string CreatedBy,
    IReadOnlyList<ProgrammeMember> Members,
    DateTimeOffset CreatedAt)
{
    public bool HasMember(string userId) => Members.Any(m => m.UserId == userId);

    public ProgrammeDto ToDto()
        => new(Id, StoreId, Name, Description, StartsOn, EndsOn, CreatedBy, Members.Select(m => m.ToDto()).ToList(), CreatedAt);
}

internal sealed record ProgrammeMember(string UserId, ProgrammeRole Role, DateTimeOffset AddedAt)
{
    public ProgrammeMemberDto ToDto() => new(UserId, Role, AddedAt);
}

/// <summary>Body of <c>POST /api/programmes</c>. Fields are nullable so the service owns validation.</summary>
internal sealed record CreateProgrammeRequest(string? Name, string? Description, DateOnly? StartsOn, DateOnly? EndsOn);

/// <summary>Body of <c>POST /api/programmes/{id}/members</c>.</summary>
internal sealed record AddProgrammeMemberRequest(string? UserId, ProgrammeRole? Role);
