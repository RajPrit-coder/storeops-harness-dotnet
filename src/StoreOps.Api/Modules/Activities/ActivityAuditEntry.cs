using StoreOps.Api.Modules.Activities.Contracts;

namespace StoreOps.Api.Modules.Activities;

/// <summary>
/// One recorded status change of an activity (BR-11). Owned by the Activities module and written only by
/// <see cref="ActivityService"/>, right after the activity itself has been saved.
/// </summary>
internal sealed record ActivityAuditEntry(
    string Id,
    string ActivityId,
    string StoreId,
    ActivityStatus PreviousStatus,
    ActivityStatus NewStatus,
    string ChangedBy,
    string Source,
    string? Note,
    DateTimeOffset OccurredAt)
{
    /// <summary>Source of entries written by <c>PATCH /api/activities/bulk-status</c>.</summary>
    public const string BulkStatusUpdateSource = "BULK_STATUS_UPDATE";

    public ActivityAuditEntryDto ToDto() => new(Id, ActivityId, PreviousStatus, NewStatus, ChangedBy, Source, Note, OccurredAt);
}

/// <summary>One item of <c>GET /api/activities/{id}/audit</c>. The store is implied by the activity and not repeated.</summary>
internal sealed record ActivityAuditEntryDto(
    string Id,
    string ActivityId,
    ActivityStatus PreviousStatus,
    ActivityStatus NewStatus,
    string ChangedBy,
    string Source,
    string? Note,
    DateTimeOffset OccurredAt);
