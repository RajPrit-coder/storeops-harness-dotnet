using StoreOps.Api.Modules.Activities.Contracts;

namespace StoreOps.Api.Modules.Activities;

/// <summary>
/// Body of <c>PATCH /api/activities/bulk-status</c>. Fields are nullable so the service owns validation
/// (status must be DONE or BLOCKED; 1–50 distinct, non-blank activity ids).
/// </summary>
internal sealed record BulkStatusUpdateRequest(
    ActivityStatus? Status,
    IReadOnlyList<string?>? ActivityIds);

/// <summary>Response of <c>PATCH /api/activities/bulk-status</c>: one result per requested id, in request order.</summary>
internal sealed record BulkStatusUpdateResponse(
    ActivityStatus Status,
    int Requested,
    int Updated,
    int Unchanged,
    int Failed,
    IReadOnlyList<BulkStatusItemResult> Results);

/// <summary>Outcome for one requested activity. Statuses are null when the item failed.</summary>
internal sealed record BulkStatusItemResult(
    string ActivityId,
    BulkStatusOutcome Outcome,
    ActivityStatus? PreviousStatus,
    ActivityStatus? NewStatus,
    BulkStatusItemError? Error);

/// <summary>Why one item of a bulk request failed, using the standard error codes.</summary>
internal sealed record BulkStatusItemError(string Code, string Message);
