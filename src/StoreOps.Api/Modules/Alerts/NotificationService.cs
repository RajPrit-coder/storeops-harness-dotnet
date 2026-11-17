using StoreOps.Api.Modules.Activities.Contracts;
using StoreOps.Api.Modules.Alerts.Contracts;
using StoreOps.Api.Modules.Staff.Contracts;
using StoreOps.Api.Shared;
using StoreOps.Api.Shared.Events;

namespace StoreOps.Api.Modules.Alerts;

/// <summary>
/// Alert use cases. Notifications are only ever created in reaction to events from the bus (see
/// AlertSubscribers); no other module may import this service.
/// </summary>
internal sealed class NotificationService(
    INotificationRepository repository,
    IStaffDirectory staffDirectory,
    IEventBus eventBus,
    TimeProvider timeProvider)
{
    /// <summary>Spec §3.6 <c>GET /api/alerts</c>: alerts addressed to the authenticated user.</summary>
    public async Task<IReadOnlyList<NotificationDto>> ListForUserAsync(CurrentUser user, CancellationToken cancellationToken)
        => (await repository.ListByRecipientAsync(user.UserId, cancellationToken)).Select(n => n.ToDto()).ToList();

    /// <summary>When an activity becomes BLOCKED, raise an ESCALATION alert to every active store manager of its store.</summary>
    public async Task<IReadOnlyList<NotificationDto>> EscalateBlockedActivityAsync(
        ActivityStatusChanged statusChanged, CancellationToken cancellationToken)
    {
        if (statusChanged.NewStatus != ActivityStatus.Blocked)
        {
            return [];
        }

        var managers = (await staffDirectory.ListByStoreAsync(statusChanged.StoreId, cancellationToken))
            .Where(s => s is { Active: true, Role: StaffRole.StoreManager });

        var created = new List<NotificationDto>();
        foreach (var manager in managers)
        {
            created.Add(await CreateAsync(
                manager.Id,
                statusChanged.StoreId,
                AlertType.Escalation,
                $"Activity '{statusChanged.Title}' ({statusChanged.ActivityId}) was marked BLOCKED.",
                statusChanged.ActivityId,
                cancellationToken));
        }

        return created;
    }

    private async Task<NotificationDto> CreateAsync(
        string recipientId, string storeId, AlertType type, string message, string? sourceActivityId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var notification = new Notification(
            Ids.New("ntf"), recipientId, storeId, type, NotificationChannel.InApp, NotificationStatus.Unread, message, sourceActivityId, now);

        await repository.SaveAsync(notification, cancellationToken);
        await eventBus.PublishAsync(new NotificationCreated(notification.Id, recipientId, storeId, type, now), cancellationToken);
        return notification.ToDto();
    }
}
