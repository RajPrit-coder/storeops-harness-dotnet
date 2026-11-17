using StoreOps.Api.Modules.Staff.Contracts;

namespace StoreOps.Api.Tests.Infrastructure;

/// <summary>Seeded users (see StaffSeedData) as authenticated callers.</summary>
internal static class TestUsers
{
    public static CurrentUser StoreManager { get; } = new("usr-sm-001", "store-001", "region-north", StaffRole.StoreManager);

    public static CurrentUser DepartmentLead { get; } = new("usr-dl-001", "store-001", "region-north", StaffRole.DepartmentLead);

    public static CurrentUser Associate { get; } = new("usr-as-001", "store-001", "region-north", StaffRole.Associate);

    public static CurrentUser OtherStoreManager { get; } = new("usr-sm-002", "store-002", "region-north", StaffRole.StoreManager);
}
