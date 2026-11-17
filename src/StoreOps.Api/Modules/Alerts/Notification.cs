using StoreOps.Api.Modules.Alerts.Contracts;

namespace StoreOps.Api.Modules.Alerts;

internal sealed record Notification(
    string Id,
    string RecipientId,
    string StoreId,
    AlertType Type,
    NotificationChannel Channel,
    NotificationStatus Status,
    string Message,
    string? SourceActivityId,
    DateTimeOffset CreatedAt)
{
    public NotificationDto ToDto() => new(Id, RecipientId, StoreId, Type, Channel, Status, Message, SourceActivityId, CreatedAt);
}
