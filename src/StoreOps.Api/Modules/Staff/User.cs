using StoreOps.Api.Modules.Staff.Contracts;

namespace StoreOps.Api.Modules.Staff;

internal sealed record User(string Id, string StoreId, string RegionId, StaffRole Role, bool Active, UserProfile Profile)
{
    public StaffMemberDto ToDto() => new(Id, StoreId, RegionId, Profile.DisplayName, Role, Profile.Department, Active);

    public CurrentUser ToCurrentUser() => new(Id, StoreId, RegionId, Role);
}

internal sealed record UserProfile(string DisplayName, string? Department, string Email);

internal sealed record AuthToken(string Value, string UserId, DateTimeOffset ExpiresAt)
{
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
