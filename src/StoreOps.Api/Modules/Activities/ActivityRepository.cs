using System.Collections.Concurrent;
using StoreOps.Api.Modules.Activities.Contracts;

namespace StoreOps.Api.Modules.Activities;

/// <summary>Data access only: no HTTP, no events, no calls to other modules.</summary>
internal interface IActivityRepository
{
    Task<Activity?> GetByIdAsync(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Activity>> ListByStoreAsync(
        string storeId, string? programmeId, ActivityStatus? status, CancellationToken cancellationToken);

    Task SaveAsync(Activity activity, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);
}

internal sealed class InMemoryActivityRepository : IActivityRepository
{
    private readonly ConcurrentDictionary<string, Activity> _activities = new(StringComparer.Ordinal);

    public Task<Activity?> GetByIdAsync(string id, CancellationToken cancellationToken)
        => Task.FromResult(_activities.GetValueOrDefault(id));

    public Task<IReadOnlyList<Activity>> ListByStoreAsync(
        string storeId, string? programmeId, ActivityStatus? status, CancellationToken cancellationToken)
    {
        IReadOnlyList<Activity> result = _activities.Values
            .Where(a => a.StoreId == storeId)
            .Where(a => programmeId is null || a.ProgrammeId == programmeId)
            .Where(a => status is null || a.Status == status)
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(result);
    }

    public Task SaveAsync(Activity activity, CancellationToken cancellationToken)
    {
        _activities[activity.Id] = activity;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
        => Task.FromResult(_activities.TryRemove(id, out _));
}
