using StoreOps.Api.Shared.Events;

namespace StoreOps.Api.Modules.Alerts.Contracts;

// Public surface of the Alerts module (spec §3.3 Notification, NotificationChannel, NotificationStatus,
// AlertType). Other modules never call NotificationService: they publish events and Alerts reacts.
public enum AlertType
{
    Inventory,
    SlaBreach,
    ShiftHandover,
    Escalation,
}

public enum NotificationChannel
{
    InApp,
    Email,
}

public enum NotificationStatus
{
    Unread,
    Read,
}

public sealed record NotificationDto(
    string Id,
    string RecipientId,
    string StoreId,
    AlertType Type,
    NotificationChannel Channel,
    NotificationStatus Status,
    string Message,
    string? SourceActivityId,
    DateTimeOffset CreatedAt);

public sealed record NotificationCreated(
    string NotificationId,
    string RecipientId,
    string StoreId,
    AlertType Type,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
