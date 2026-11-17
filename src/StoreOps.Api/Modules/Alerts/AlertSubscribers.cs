using StoreOps.Api.Modules.Activities.Contracts;
using StoreOps.Api.Shared.Events;

namespace StoreOps.Api.Modules.Alerts;

/// <summary>Inbound adapter from the event bus. Like a route, it delegates straight to the service.</summary>
internal sealed class ActivityBlockedSubscriber(NotificationService notificationService) : IEventSubscriber<ActivityStatusChanged>
{
    public Task HandleAsync(ActivityStatusChanged integrationEvent, CancellationToken cancellationToken)
        => notificationService.EscalateBlockedActivityAsync(integrationEvent, cancellationToken);
}
