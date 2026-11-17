using StoreOps.Api.Modules.Staff.Contracts;

namespace StoreOps.Api.Modules.Staff;

/// <summary>Staff use cases: authentication plus the read-only <see cref="IStaffDirectory"/>.</summary>
internal sealed class StaffService(IStaffRepository repository, TimeProvider timeProvider) : IStaffDirectory
{
    public async Task<StaffMemberDto?> FindByIdAsync(string userId, CancellationToken cancellationToken)
        => (await repository.GetUserByIdAsync(userId, cancellationToken))?.ToDto();

    public async Task<IReadOnlyList<StaffMemberDto>> ListByStoreAsync(string storeId, CancellationToken cancellationToken)
        => (await repository.ListUsersByStoreAsync(storeId, cancellationToken)).Select(u => u.ToDto()).ToList();

    /// <summary>Resolves a bearer token to its user; <c>null</c> when unknown, expired or the user is inactive.</summary>
    public async Task<CurrentUser?> AuthenticateAsync(string tokenValue, CancellationToken cancellationToken)
    {
        var token = await repository.FindTokenAsync(tokenValue, cancellationToken);
        if (token is null || token.IsExpired(timeProvider.GetUtcNow()))
        {
            return null;
        }

        var user = await repository.GetUserByIdAsync(token.UserId, cancellationToken);
        return user is { Active: true } ? user.ToCurrentUser() : null;
    }
}
