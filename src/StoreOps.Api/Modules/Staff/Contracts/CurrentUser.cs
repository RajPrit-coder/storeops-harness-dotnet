using System.Security.Claims;
using StoreOps.Api.Shared.Errors;

namespace StoreOps.Api.Modules.Staff.Contracts;

/// <summary>
/// The authenticated caller. Route handlers declare a <c>CurrentUser</c> parameter and minimal APIs bind
/// it from the principal that the Staff authentication handler set up.
/// </summary>
public sealed record CurrentUser(string UserId, string StoreId, string RegionId, StaffRole Role)
{
    public bool IsStoreManagerOf(string storeId) => Role == StaffRole.StoreManager && StoreId == storeId;

    public static ValueTask<CurrentUser?> BindAsync(HttpContext context)
        => ValueTask.FromResult<CurrentUser?>(FromPrincipal(context.User));

    public static CurrentUser FromPrincipal(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(StaffClaimTypes.UserId);
        var storeId = principal.FindFirstValue(StaffClaimTypes.StoreId);
        var regionId = principal.FindFirstValue(StaffClaimTypes.RegionId);
        var role = principal.FindFirstValue(StaffClaimTypes.Role);

        if (userId is null || storeId is null || regionId is null || !Enum.TryParse<StaffRole>(role, out var staffRole))
        {
            throw new UnauthorizedError();
        }

        return new CurrentUser(userId, storeId, regionId, staffRole);
    }
}

public static class StaffClaimTypes
{
    public const string UserId = ClaimTypes.NameIdentifier;
    public const string Role = ClaimTypes.Role;
    public const string StoreId = "storeops:store_id";
    public const string RegionId = "storeops:region_id";
}
