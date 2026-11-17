namespace StoreOps.Api.Modules.Reports;

public static class ReportsModule
{
    public static IServiceCollection AddReportsModule(this IServiceCollection services)
    {
        services.AddSingleton<IReportRepository, InMemoryReportRepository>();
        services.AddScoped<ReportService>();
        return services;
    }

    public static IEndpointRouteBuilder MapReportsModule(this IEndpointRouteBuilder app) => app.MapReportRoutes();
}
