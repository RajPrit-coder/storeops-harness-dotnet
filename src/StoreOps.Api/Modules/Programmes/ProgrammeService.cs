using StoreOps.Api.Modules.Programmes.Contracts;
using StoreOps.Api.Modules.Staff.Contracts;
using StoreOps.Api.Shared;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Shared.Events;

namespace StoreOps.Api.Modules.Programmes;

internal sealed class ProgrammeService(
    IProgrammeRepository repository,
    IStaffDirectory staffDirectory,
    IEventBus eventBus,
    TimeProvider timeProvider) : IProgrammeQueries
{
    public async Task<ProgrammeDto?> FindByIdAsync(string programmeId, CancellationToken cancellationToken)
        => (await repository.GetByIdAsync(programmeId, cancellationToken))?.ToDto();

    /// <summary>Programmes of the caller's store only.</summary>
    public async Task<IReadOnlyList<ProgrammeDto>> ListForStoreAsync(CurrentUser user, CancellationToken cancellationToken)
        => (await repository.ListByStoreAsync(user.StoreId, cancellationToken)).Select(p => p.ToDto()).ToList();

    public async Task<ProgrammeDto> CreateAsync(CurrentUser user, CreateProgrammeRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw new ValidationError("Programme name is required.", "name");
        }

        if (request is { StartsOn: { } startsOn, EndsOn: { } endsOn } && endsOn < startsOn)
        {
            throw new ValidationError("Programme end date must not be before its start date.", "endsOn");
        }

        if (await repository.NameExistsInStoreAsync(user.StoreId, name, cancellationToken))
        {
            throw new ConflictError($"Store '{user.StoreId}' already has a programme named '{name}'.");
        }

        var now = timeProvider.GetUtcNow();
        var programme = new Programme(
            Ids.New("prg"), user.StoreId, name, request.Description, request.StartsOn, request.EndsOn, user.UserId, [], now);

        await repository.SaveAsync(programme, cancellationToken);
        await eventBus.PublishAsync(new ProgrammeCreated(programme.Id, programme.StoreId, user.UserId, now), cancellationToken);
        return programme.ToDto();
    }

    public async Task<ProgrammeMemberDto> AddMemberAsync(
        CurrentUser user, string programmeId, AddProgrammeMemberRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new ValidationError("userId is required.", "userId");
        }

        if (request.Role is not { } role)
        {
            throw new ValidationError("role is required.", "role");
        }

        var programme = await GetVisibleAsync(user, programmeId, cancellationToken);
        var memberId = request.UserId.Trim();

        // Cross-module read through the Staff service contract (allowed: read-only lookup).
        var staff = await staffDirectory.FindByIdAsync(memberId, cancellationToken)
            ?? throw new NotFoundError("Staff member", memberId);

        if (staff.StoreId != programme.StoreId)
        {
            throw new BusinessRuleError($"Staff member '{memberId}' does not belong to store '{programme.StoreId}'.");
        }

        if (!staff.Active)
        {
            throw new BusinessRuleError($"Staff member '{memberId}' is inactive and cannot join a programme.");
        }

        if (programme.HasMember(memberId))
        {
            throw new ConflictError($"Staff member '{memberId}' is already a member of programme '{programmeId}'.");
        }

        var now = timeProvider.GetUtcNow();
        var member = new ProgrammeMember(memberId, role, now);

        await repository.SaveAsync(programme with { Members = [.. programme.Members, member] }, cancellationToken);
        await eventBus.PublishAsync(
            new ProgrammeMemberAdded(programme.Id, programme.StoreId, memberId, role, user.UserId, now), cancellationToken);
        return member.ToDto();
    }

    /// <summary>A programme in another store is reported as not found so its existence is not leaked.</summary>
    private async Task<Programme> GetVisibleAsync(CurrentUser user, string programmeId, CancellationToken cancellationToken)
    {
        var programme = await repository.GetByIdAsync(programmeId, cancellationToken);
        return programme is not null && programme.StoreId == user.StoreId
            ? programme
            : throw new NotFoundError("Programme", programmeId);
    }
}
