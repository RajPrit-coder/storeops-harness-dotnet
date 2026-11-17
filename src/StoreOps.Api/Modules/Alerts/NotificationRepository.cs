using System.Collections.Concurrent;

namespace StoreOps.Api.Modules.Alerts;

internal interface INotificationRepository
{
    Task<IReadOnlyList<Notification>> ListByRecipientAsync(string recipientId, CancellationToken cancellationToken);

    Task SaveAsync(Notification notification, CancellationToken cancellationToken);
}

internal sealed class InMemoryNotificationRepository : INotificationRepository
{
    private readonly ConcurrentDictionary<string, Notification> _notifications = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<Notification>> ListByRecipientAsync(string recipientId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Notification> result = _notifications.Values
            .Where(n => n.RecipientId == recipientId)
            .OrderByDescending(n => n.CreatedAt)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(result);
    }

    public Task SaveAsync(Notification notification, CancellationToken cancellationToken)
    {
        _notifications[notification.Id] = notification;
        return Task.CompletedTask;
    }
}
