using Microsoft.AspNetCore.Http.HttpResults;
using StoreOps.Api.Modules.Activities.Contracts;
using StoreOps.Api.Modules.Staff.Contracts;
using StoreOps.Api.Shared.Http;

namespace StoreOps.Api.Modules.Activities;

/// <summary>HTTP layer only: binds and validates input, calls <see cref="ActivityService"/>, shapes the response.</summary>
internal static class ActivityRoutes
{
    public static IEndpointRouteBuilder MapActivityRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/activities").WithTags("Activities");

        group.MapGet("/", ListActivitiesAsync).WithName("ListActivities");
        group.MapPost("/", CreateActivityAsync).WithName("CreateActivity");
        group.MapGet("/{id}", GetActivityAsync).WithName("GetActivity");
        group.MapPatch("/{id}", UpdateActivityAsync).WithName("UpdateActivity");
        group.MapDelete("/{id}", DeleteActivityAsync).WithName("DeleteActivity");

        return app;
    }

    private static async Task<Ok<IReadOnlyList<ActivityDto>>> ListActivitiesAsync(
        ActivityService service, CurrentUser user, string? programmeId, string? status, CancellationToken cancellationToken)
    {
        var statusFilter = StoreOpsJson.ParseQuery<ActivityStatus>(status, "status");
        return TypedResults.Ok(await service.ListAsync(user, programmeId, statusFilter, cancellationToken));
    }

    private static async Task<Created<ActivityDto>> CreateActivityAsync(
        ActivityService service, CurrentUser user, CreateActivityRequest request, CancellationToken cancellationToken)
    {
        var activity = await service.CreateAsync(user, request, cancellationToken);
        return TypedResults.Created($"/api/activities/{activity.Id}", activity);
    }

    private static async Task<Ok<ActivityDto>> GetActivityAsync(
        ActivityService service, CurrentUser user, string id, CancellationToken cancellationToken)
        => TypedResults.Ok(await service.GetAsync(user, id, cancellationToken));

    private static async Task<Ok<ActivityDto>> UpdateActivityAsync(
        ActivityService service, CurrentUser user, string id, UpdateActivityRequest request, CancellationToken cancellationToken)
        => TypedResults.Ok(await service.UpdateAsync(user, id, request, cancellationToken));

    private static async Task<NoContent> DeleteActivityAsync(
        ActivityService service, CurrentUser user, string id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(user, id, cancellationToken);
        return TypedResults.NoContent();
    }
}
