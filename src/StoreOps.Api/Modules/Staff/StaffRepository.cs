using System.Collections.Concurrent;

namespace StoreOps.Api.Modules.Staff;

internal interface IStaffRepository
{
    Task<User?> GetUserByIdAsync(string userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<User>> ListUsersByStoreAsync(string storeId, CancellationToken cancellationToken);

    Task<AuthToken?> FindTokenAsync(string tokenValue, CancellationToken cancellationToken);
}

/// <summary>In-memory staff store seeded from <see cref="StaffSeedData"/>.</summary>
internal sealed class InMemoryStaffRepository : IStaffRepository
{
    private readonly ConcurrentDictionary<string, User> _users = new(StaffSeedData.Users.ToDictionary(u => u.Id), StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AuthToken> _tokens = new(StaffSeedData.Tokens.ToDictionary(t => t.Value), StringComparer.Ordinal);

    public Task<User?> GetUserByIdAsync(string userId, CancellationToken cancellationToken)
        => Task.FromResult(_users.GetValueOrDefault(userId));

    public Task<IReadOnlyList<User>> ListUsersByStoreAsync(string storeId, CancellationToken cancellationToken)
    {
        IReadOnlyList<User> users = _users.Values
            .Where(u => u.StoreId == storeId)
            .OrderBy(u => u.Id, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(users);
    }

    public Task<AuthToken?> FindTokenAsync(string tokenValue, CancellationToken cancellationToken)
        => Task.FromResult(_tokens.GetValueOrDefault(tokenValue));
}
