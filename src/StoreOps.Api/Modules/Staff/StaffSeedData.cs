using StoreOps.Api.Modules.Staff.Contracts;

namespace StoreOps.Api.Modules.Staff;

/// <summary>Demo users and bearer tokens for the in-memory store (no registration endpoint exists yet).</summary>
internal static class StaffSeedData
{
    public const string StoreManagerToken = "dev-token-store-manager";
    public const string DepartmentLeadToken = "dev-token-department-lead";
    public const string AssociateToken = "dev-token-associate";
    public const string OtherStoreManagerToken = "dev-token-store-002-manager";
    public const string RegionalManagerToken = "dev-token-regional-manager";
    public const string ExpiredToken = "dev-token-expired";
    public const string InactiveUserToken = "dev-token-inactive";

    private static readonly DateTimeOffset NeverExpires = new(2099, 12, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LongExpired = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<User> Users { get; } =
    [
        new("usr-sm-001", "store-001", "region-north", StaffRole.StoreManager, true, new("Avery Patel", null, "avery.patel@storeops.example")),
        new("usr-dl-001", "store-001", "region-north", StaffRole.DepartmentLead, true, new("Jordan Lee", "GROCERY", "jordan.lee@storeops.example")),
        new("usr-as-001", "store-001", "region-north", StaffRole.Associate, true, new("Sam Okafor", "GROCERY", "sam.okafor@storeops.example")),
        new("usr-as-002", "store-001", "region-north", StaffRole.Associate, false, new("Riley Chen", "BAKERY", "riley.chen@storeops.example")),
        new("usr-sm-002", "store-002", "region-north", StaffRole.StoreManager, true, new("Morgan Diaz", null, "morgan.diaz@storeops.example")),
        new("usr-rm-001", "store-001", "region-north", StaffRole.RegionalManager, true, new("Casey Brooks", null, "casey.brooks@storeops.example")),
    ];

    public static IReadOnlyList<AuthToken> Tokens { get; } =
    [
        new(StoreManagerToken, "usr-sm-001", NeverExpires),
        new(DepartmentLeadToken, "usr-dl-001", NeverExpires),
        new(AssociateToken, "usr-as-001", NeverExpires),
        new(OtherStoreManagerToken, "usr-sm-002", NeverExpires),
        new(RegionalManagerToken, "usr-rm-001", NeverExpires),
        new(ExpiredToken, "usr-as-001", LongExpired),
        new(InactiveUserToken, "usr-as-002", NeverExpires),
    ];
}
