using Microsoft.AspNetCore.Http.HttpResults;
using StoreOps.Api.Modules.Alerts.Contracts;
using StoreOps.Api.Modules.Staff.Contracts;

namespace StoreOps.Api.Modules.Alerts;

/// <summary>HTTP layer only: calls <see cref="NotificationService"/> and shapes the response.</summary>
internal static class NotificationRoutes
{
    public static IEndpointRouteBuilder MapNotificationRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/alerts").WithTags("Alerts");

        group.MapGet("/", ListAlertsAsync).WithName("ListAlerts");

        return app;
    }

    private static async Task<Ok<IReadOnlyList<NotificationDto>>> ListAlertsAsync(
        NotificationService service, CurrentUser user, CancellationToken cancellationToken)
        => TypedResults.Ok(await service.ListForUserAsync(user, cancellationToken));
}
