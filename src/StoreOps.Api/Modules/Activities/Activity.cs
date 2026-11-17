using StoreOps.Api.Modules.Activities.Contracts;

namespace StoreOps.Api.Modules.Activities;

internal sealed record Activity(
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
    DateTimeOffset UpdatedAt)
{
    public ActivityDto ToDto() => new(
        Id, StoreId, ProgrammeId, Title, Description, Status, Priority, Category, Department, AssigneeId, OwnerId, DueAt, CreatedAt, UpdatedAt);
}

/// <summary>Body of <c>POST /api/activities</c>. Fields are nullable so the service owns validation.</summary>
internal sealed record CreateActivityRequest(
    string? Title,
    string? Description,
    string? ProgrammeId,
    ActivityPriority? Priority,
    ActivityCategory? Category,
    string? Department,
    string? AssigneeId,
    DateTimeOffset? DueAt);

/// <summary>Body of <c>PATCH /api/activities/{id}</c>. Omitted fields are left unchanged.</summary>
internal sealed record UpdateActivityRequest(
    ActivityStatus? Status,
    ActivityPriority? Priority,
    ActivityCategory? Category,
    string? AssigneeId)
{
    public bool IsEmpty => Status is null && Priority is null && Category is null && AssigneeId is null;
}
