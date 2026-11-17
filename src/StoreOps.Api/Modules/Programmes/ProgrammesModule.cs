using StoreOps.Api.Modules.Programmes.Contracts;

namespace StoreOps.Api.Modules.Programmes;

public static class ProgrammesModule
{
    public static IServiceCollection AddProgrammesModule(this IServiceCollection services)
    {
        services.AddSingleton<IProgrammeRepository, InMemoryProgrammeRepository>();
        services.AddScoped<ProgrammeService>();
        services.AddScoped<IProgrammeQueries>(sp => sp.GetRequiredService<ProgrammeService>());
        return services;
    }

    public static IEndpointRouteBuilder MapProgrammesModule(this IEndpointRouteBuilder app) => app.MapProgrammeRoutes();
}
