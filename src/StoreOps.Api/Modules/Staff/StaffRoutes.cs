namespace StoreOps.Api.Modules.Staff;

/// <summary>
/// Staff is auth-only in the base API surface (spec §3.6), so it maps no endpoints yet. Future staff
/// endpoints belong here and must call <see cref="StaffService"/>, never the repository.
/// </summary>
internal static class StaffRoutes
{
    public static IEndpointRouteBuilder MapStaffRoutes(this IEndpointRouteBuilder app) => app;
}
