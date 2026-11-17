namespace StoreOps.Api.Shared.Events;

public static class EventBusServiceCollectionExtensions
{
    public static IServiceCollection AddInMemoryEventBus(this IServiceCollection services)
        => services.AddSingleton<IEventBus, InMemoryEventBus>();

    public static IServiceCollection AddEventSubscriber<TEvent, TSubscriber>(this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where TSubscriber : class, IEventSubscriber<TEvent>
        => services.AddScoped<IEventSubscriber<TEvent>, TSubscriber>();
}
