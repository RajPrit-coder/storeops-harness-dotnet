using StoreOps.Api.Modules.Activities.Contracts;

namespace StoreOps.Api.Modules.Activities;

public static class ActivitiesModule
{
    public static IServiceCollection AddActivitiesModule(this IServiceCollection services)
    {
        services.AddSingleton<IActivityRepository, InMemoryActivityRepository>();
        services.AddScoped<ActivityService>();
        services.AddScoped<IActivityQueries>(sp => sp.GetRequiredService<ActivityService>());
        return services;
    }

    public static IEndpointRouteBuilder MapActivitiesModule(this IEndpointRouteBuilder app) => app.MapActivityRoutes();
}
