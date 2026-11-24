using System.Collections.Concurrent;

namespace StoreOps.Api.Modules.Activities;

/// <summary>Append-only store of activity audit entries. Data access only: no HTTP, no events, no other modules.</summary>
internal interface IActivityAuditRepository
{
    Task AppendAsync(ActivityAuditEntry entry, CancellationToken cancellationToken);

    /// <summary>Entries of one activity, oldest first (in the order they were appended).</summary>
    Task<IReadOnlyList<ActivityAuditEntry>> ListByActivityAsync(string activityId, CancellationToken cancellationToken);
}

internal sealed class InMemoryActivityAuditRepository : IActivityAuditRepository
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ActivityAuditEntry>> _entries = new(StringComparer.Ordinal);

    public Task AppendAsync(ActivityAuditEntry entry, CancellationToken cancellationToken)
    {
        _entries.GetOrAdd(entry.ActivityId, _ => new ConcurrentQueue<ActivityAuditEntry>()).Enqueue(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ActivityAuditEntry>> ListByActivityAsync(string activityId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ActivityAuditEntry> result = _entries.TryGetValue(activityId, out var queue) ? [.. queue] : [];
        return Task.FromResult(result);
    }
}
