namespace StoreOps.Api.Shared.Events;

/// <summary>
/// In-process, synchronous event bus. Subscribers run one after another in a fresh DI scope. A failing
/// subscriber is logged and does not fail the publisher or the other subscribers.
/// Services publish AFTER saving state, so delivery deliberately ignores the caller's cancellation token:
/// a client abort must not leave the state saved but its side effects (e.g. an alert) dropped.
/// Known limit of the stub: no outbox, so a process crash between save and publish loses the event.
/// </summary>
internal sealed partial class InMemoryEventBus(IServiceScopeFactory scopeFactory, ILogger<InMemoryEventBus> logger) : IEventBus
{
    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var subscribers = scope.ServiceProvider.GetServices<IEventSubscriber<TEvent>>();

        foreach (var subscriber in subscribers)
        {
            try
            {
                await subscriber.HandleAsync(integrationEvent, CancellationToken.None);
            }
#pragma warning disable CA1031 // Isolation boundary: subscriber failures must not propagate to publishers.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                LogSubscriberFailed(logger, ex, subscriber.GetType().Name, typeof(TEvent).Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Event subscriber {Subscriber} failed for {EventName}")]
    private static partial void LogSubscriberFailed(ILogger logger, Exception exception, string subscriber, string eventName);
}
