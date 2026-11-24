using StoreOps.Api.Modules.Activities.Contracts;
using StoreOps.Api.Modules.Programmes.Contracts;
using StoreOps.Api.Modules.Staff.Contracts;
using StoreOps.Api.Shared;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Shared.Events;

namespace StoreOps.Api.Modules.Activities;

/// <summary>
/// Activity business rules. Reads other modules only through their query contracts and announces every
/// state change on the event bus; it never calls alerts or reports directly.
/// </summary>
internal sealed class ActivityService(
    IActivityRepository repository,
    IStaffDirectory staffDirectory,
    IProgrammeQueries programmeQueries,
    IEventBus eventBus,
    TimeProvider timeProvider) : IActivityQueries
{
    /// <summary>Upper bound on the number of activities one bulk status request may change.</summary>
    public const int MaxBulkActivityIds = 50;

    public async Task<ActivityDto?> FindByIdAsync(string activityId, CancellationToken cancellationToken)
        => (await repository.GetByIdAsync(activityId, cancellationToken))?.ToDto();

    public Task<IReadOnlyList<ActivityDto>> ListByStoreAsync(string storeId, CancellationToken cancellationToken)
        => ListAsync(storeId, programmeId: null, status: null, cancellationToken);

    public Task<IReadOnlyList<ActivityDto>> ListAsync(
        CurrentUser user, string? programmeId, ActivityStatus? status, CancellationToken cancellationToken)
        => ListAsync(user.StoreId, string.IsNullOrWhiteSpace(programmeId) ? null : programmeId.Trim(), status, cancellationToken);

    public async Task<ActivityDto> GetAsync(CurrentUser user, string id, CancellationToken cancellationToken)
        => (await GetVisibleAsync(user, id, cancellationToken)).ToDto();

    public async Task<ActivityDto> CreateAsync(CurrentUser user, CreateActivityRequest request, CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            throw new ValidationError("Activity title is required.", "title");
        }

        if (request.ProgrammeId is { } programmeId)
        {
            var programme = await programmeQueries.FindByIdAsync(programmeId, cancellationToken);
            if (programme is null || programme.StoreId != user.StoreId)
            {
                throw new NotFoundError("Programme", programmeId);
            }
        }

        if (request.AssigneeId is { } assigneeId)
        {
            await EnsureAssignableAsync(user.StoreId, assigneeId, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var activity = new Activity(
            Ids.New("act"),
            user.StoreId,
            request.ProgrammeId,
            title,
            request.Description,
            ActivityStatus.Todo,
            request.Priority ?? ActivityPriority.Medium,
            request.Category ?? ActivityCategory.General,
            request.Department,
            request.AssigneeId,
            user.UserId,
            request.DueAt,
            now,
            now);

        await repository.SaveAsync(activity, cancellationToken);
        await eventBus.PublishAsync(
            new ActivityCreated(activity.Id, activity.StoreId, activity.ProgrammeId, activity.OwnerId, activity.Priority, activity.Category, now),
            cancellationToken);

        if (activity.AssigneeId is { } assigned)
        {
            await eventBus.PublishAsync(
                new ActivityAssigned(activity.Id, activity.StoreId, null, assigned, user.UserId, now), cancellationToken);
        }

        return activity.ToDto();
    }

    public async Task<ActivityDto> UpdateAsync(
        CurrentUser user, string id, UpdateActivityRequest request, CancellationToken cancellationToken)
    {
        if (request.IsEmpty)
        {
            throw new ValidationError("Provide at least one of status, priority, category or assigneeId.");
        }

        var current = await GetVisibleAsync(user, id, cancellationToken);

        if (current.Status == ActivityStatus.Done && request.Status is { } next && next != ActivityStatus.Done)
        {
            throw new BusinessRuleError($"Activity '{id}' is DONE and cannot be reopened.");
        }

        if (request.AssigneeId is { } assigneeId && assigneeId != current.AssigneeId)
        {
            await EnsureAssignableAsync(current.StoreId, assigneeId, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var updated = current with
        {
            Status = request.Status ?? current.Status,
            Priority = request.Priority ?? current.Priority,
            Category = request.Category ?? current.Category,
            AssigneeId = request.AssigneeId ?? current.AssigneeId,
            UpdatedAt = now,
        };

        await repository.SaveAsync(updated, cancellationToken);

        if (updated.Status != current.Status)
        {
            await eventBus.PublishAsync(
                new ActivityStatusChanged(
                    updated.Id, updated.StoreId, updated.Title, current.Status, updated.Status, updated.Priority, updated.AssigneeId, user.UserId, now),
                cancellationToken);
        }

        if (updated.AssigneeId is { } newAssignee && newAssignee != current.AssigneeId)
        {
            await eventBus.PublishAsync(
                new ActivityAssigned(updated.Id, updated.StoreId, current.AssigneeId, newAssignee, user.UserId, now), cancellationToken);
        }

        return updated.ToDto();
    }

    /// <summary>Spec §3.6: only the activity's owner or a store manager of its store may delete it.</summary>
    public async Task DeleteAsync(CurrentUser user, string id, CancellationToken cancellationToken)
    {
        var activity = await GetVisibleAsync(user, id, cancellationToken);

        if (activity.OwnerId != user.UserId && !user.IsStoreManagerOf(activity.StoreId))
        {
            throw new ForbiddenError("Only the activity owner or a store manager can delete this activity.");
        }

        await repository.DeleteAsync(activity.Id, cancellationToken);
        await eventBus.PublishAsync(
            new ActivityDeleted(activity.Id, activity.StoreId, user.UserId, timeProvider.GetUtcNow()), cancellationToken);
    }

    /// <summary>
    /// Shift handover: sets every listed activity of the caller's store to DONE or BLOCKED in one request.
    /// The whole request is validated before any activity is touched; ids are then processed one at a time,
    /// in request order. An activity already in the target status is reported UNCHANGED and left untouched;
    /// every real change is saved and announced with its own <see cref="ActivityStatusChanged"/>.
    /// </summary>
    public async Task<BulkStatusUpdateResponse> BulkUpdateStatusAsync(
        CurrentUser user, BulkStatusUpdateRequest request, CancellationToken cancellationToken)
    {
        var target = ValidateBulkTargetStatus(request.Status);
        var activityIds = ValidateBulkActivityIds(request.ActivityIds);

        var results = new List<BulkStatusItemResult>(activityIds.Count);
        foreach (var activityId in activityIds)
        {
            results.Add(await ApplyBulkStatusAsync(user, activityId, target, cancellationToken));
        }

        return new BulkStatusUpdateResponse(
            target,
            results.Count,
            results.Count(r => r.Outcome == BulkStatusOutcome.Updated),
            results.Count(r => r.Outcome == BulkStatusOutcome.Unchanged),
            results.Count(r => r.Outcome == BulkStatusOutcome.Failed),
            results);
    }

    private static ActivityStatus ValidateBulkTargetStatus(ActivityStatus? status) => status switch
    {
        ActivityStatus.Done or ActivityStatus.Blocked => status.Value,
        null => throw new ValidationError("status is required and must be DONE or BLOCKED.", "status"),
        _ => throw new ValidationError("status must be DONE or BLOCKED for a bulk status update.", "status"),
    };

    private static List<string> ValidateBulkActivityIds(IReadOnlyList<string?>? activityIds)
    {
        if (activityIds is null || activityIds.Count == 0)
        {
            throw new ValidationError("activityIds must list at least one activity id.", "activityIds");
        }

        if (activityIds.Count > MaxBulkActivityIds)
        {
            throw new ValidationError($"activityIds may list at most {MaxBulkActivityIds} activity ids.", "activityIds");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ids = new List<string>(activityIds.Count);
        foreach (var activityId in activityIds)
        {
            if (string.IsNullOrWhiteSpace(activityId))
            {
                throw new ValidationError("activityIds must not contain blank ids.", "activityIds");
            }

            if (!seen.Add(activityId))
            {
                throw new ValidationError($"activityIds lists '{activityId}' more than once.", "activityIds");
            }

            ids.Add(activityId);
        }

        return ids;
    }

    /// <summary>One bulk item: store-scoped lookup, then either UNCHANGED or the regular single-activity update.</summary>
    private async Task<BulkStatusItemResult> ApplyBulkStatusAsync(
        CurrentUser user, string activityId, ActivityStatus target, CancellationToken cancellationToken)
    {
        var current = await GetVisibleAsync(user, activityId, cancellationToken);
        if (current.Status == target)
        {
            return new BulkStatusItemResult(activityId, BulkStatusOutcome.Unchanged, current.Status, current.Status, null);
        }

        var updated = await UpdateAsync(user, activityId, new UpdateActivityRequest(target, null, null, null), cancellationToken);
        return new BulkStatusItemResult(activityId, BulkStatusOutcome.Updated, current.Status, updated.Status, null);
    }

    private async Task<IReadOnlyList<ActivityDto>> ListAsync(
        string storeId, string? programmeId, ActivityStatus? status, CancellationToken cancellationToken)
        => (await repository.ListByStoreAsync(storeId, programmeId, status, cancellationToken)).Select(a => a.ToDto()).ToList();

    /// <summary>An activity in another store is reported as not found so its existence is not leaked.</summary>
    private async Task<Activity> GetVisibleAsync(CurrentUser user, string id, CancellationToken cancellationToken)
    {
        var activity = await repository.GetByIdAsync(id, cancellationToken);
        return activity is not null && activity.StoreId == user.StoreId
            ? activity
            : throw new NotFoundError("Activity", id);
    }

    private async Task EnsureAssignableAsync(string storeId, string assigneeId, CancellationToken cancellationToken)
    {
        var assignee = await staffDirectory.FindByIdAsync(assigneeId, cancellationToken)
            ?? throw new NotFoundError("Staff member", assigneeId);

        if (assignee.StoreId != storeId)
        {
            throw new BusinessRuleError($"Staff member '{assigneeId}' does not work at store '{storeId}'.");
        }

        if (!assignee.Active)
        {
            throw new BusinessRuleError($"Staff member '{assigneeId}' is inactive and cannot be assigned.");
        }
    }
}
