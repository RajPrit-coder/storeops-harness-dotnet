namespace StoreOps.Api.Modules.Reports;

/// <summary>
/// Reports has no endpoints in the base API surface (spec §3.6); it arrives with the regional rollup
/// demonstration feature (spec §3.4). New endpoints belong here and must call <see cref="ReportService"/>.
/// </summary>
internal static class ReportRoutes
{
    public static IEndpointRouteBuilder MapReportRoutes(this IEndpointRouteBuilder app) => app;
}
