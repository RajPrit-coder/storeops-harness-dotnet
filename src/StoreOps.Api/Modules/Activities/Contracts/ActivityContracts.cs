using StoreOps.Api.Shared.Events;

namespace StoreOps.Api.Modules.Activities.Contracts;

// Public surface of the Activities module (spec §3.3 Task / TaskStatus / TaskPriority / TaskCategory,
// named Activity* in C# because Task and TaskStatus collide with System.Threading.Tasks).
// Wire values match the spec exactly: TODO | IN_PROGRESS | DONE | BLOCKED, etc.
public enum ActivityStatus
{
    Todo,
    InProgress,
    Done,
    Blocked,
}

public enum ActivityPriority
{
    Low,
    Medium,
    High,
    Critical,
}

public enum ActivityCategory
{
    Restocking,
    Planogram,
    Audit,
    Compliance,
    General,
}

/// <summary>Read-only activity lookups for other modules (e.g. reports), implemented by ActivityService.</summary>
public interface IActivityQueries
{
    Task<ActivityDto?> FindByIdAsync(string activityId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActivityDto>> ListByStoreAsync(string storeId, CancellationToken cancellationToken);
}

public sealed record ActivityDto(
    string Id,
    string StoreId,
    string? ProgrammeId,
    string Title,
    string? Description,
    ActivityStatus Status,
    ActivityPriority Priority,
    ActivityCategory Category,
    string? Department,
    string? AssigneeId,
    string OwnerId,
    DateTimeOffset? DueAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// Integration events. Subscribers (alerts, reports) depend on these records only.
public sealed record ActivityCreated(
    string ActivityId,
    string StoreId,
    string? ProgrammeId,
    string OwnerId,
    ActivityPriority Priority,
    ActivityCategory Category,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

public sealed record ActivityStatusChanged(
    string ActivityId,
    string StoreId,
    string Title,
    ActivityStatus PreviousStatus,
    ActivityStatus NewStatus,
    ActivityPriority Priority,
    string? AssigneeId,
    string ChangedBy,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

public sealed record ActivityAssigned(
    string ActivityId,
    string StoreId,
    string? PreviousAssigneeId,
    string AssigneeId,
    string AssignedBy,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

public sealed record ActivityDeleted(
    string ActivityId,
    string StoreId,
    string DeletedBy,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
