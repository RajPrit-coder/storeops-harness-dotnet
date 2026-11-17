namespace StoreOps.Api.Modules.Staff.Contracts;

// Public surface of the Staff module (spec §3.3: User, UserProfile, StaffRole, AuthToken).
// Staff is read-only to every other module: this namespace exposes lookups only, never mutations.
public enum StaffRole
{
    RegionalManager,
    StoreManager,
    DepartmentLead,
    Associate,
}

/// <summary>Read-only staff lookups for other modules, implemented by StaffService.</summary>
public interface IStaffDirectory
{
    Task<StaffMemberDto?> FindByIdAsync(string userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StaffMemberDto>> ListByStoreAsync(string storeId, CancellationToken cancellationToken);
}

public sealed record StaffMemberDto(
    string Id,
    string StoreId,
    string RegionId,
    string DisplayName,
    StaffRole Role,
    string? Department,
    bool Active);
