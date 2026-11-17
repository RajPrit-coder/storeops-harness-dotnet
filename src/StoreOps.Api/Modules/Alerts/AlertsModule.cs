using StoreOps.Api.Modules.Activities.Contracts;
using StoreOps.Api.Shared.Events;

namespace StoreOps.Api.Modules.Alerts;

public static class AlertsModule
{
    public static IServiceCollection AddAlertsModule(this IServiceCollection services)
    {
        services.AddSingleton<INotificationRepository, InMemoryNotificationRepository>();
        services.AddScoped<NotificationService>();
        services.AddEventSubscriber<ActivityStatusChanged, ActivityBlockedSubscriber>();
        return services;
    }

    public static IEndpointRouteBuilder MapAlertsModule(this IEndpointRouteBuilder app) => app.MapNotificationRoutes();
}
