namespace StoreOps.Api.Shared.Events;

/// <summary>Marker for events that cross module boundaries via <see cref="IEventBus"/>.</summary>
public interface IIntegrationEvent
{
    DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// The only channel for cross-module side effects (spec §3.5 "Event bus only"). A publisher never
/// knows who handles an event, so it can never import NotificationService or ReportService.
/// </summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;
}

/// <summary>Handles one integration event type. Subscribers are thin: they delegate to their module's service.</summary>
public interface IEventSubscriber<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
