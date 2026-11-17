using Microsoft.AspNetCore.Authentication;
using StoreOps.Api.Modules.Staff.Contracts;

namespace StoreOps.Api.Modules.Staff;

public static class StaffModule
{
    public static IServiceCollection AddStaffModule(this IServiceCollection services)
    {
        services.AddSingleton<IStaffRepository, InMemoryStaffRepository>();
        services.AddScoped<StaffService>();
        services.AddScoped<IStaffDirectory>(sp => sp.GetRequiredService<StaffService>());

        services.AddAuthentication(StaffAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, StaffAuthenticationHandler>(StaffAuthenticationHandler.SchemeName, configureOptions: null);

        return services;
    }

    public static IEndpointRouteBuilder MapStaffModule(this IEndpointRouteBuilder app) => app.MapStaffRoutes();
}
