using System.Collections.Concurrent;

namespace StoreOps.Api.Modules.Programmes;

internal interface IProgrammeRepository
{
    Task<Programme?> GetByIdAsync(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Programme>> ListByStoreAsync(string storeId, CancellationToken cancellationToken);

    Task<bool> NameExistsInStoreAsync(string storeId, string name, CancellationToken cancellationToken);

    Task SaveAsync(Programme programme, CancellationToken cancellationToken);
}

internal sealed class InMemoryProgrammeRepository : IProgrammeRepository
{
    private static readonly DateTimeOffset SeededAt = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly ConcurrentDictionary<string, Programme> _programmes = new(
        new[]
        {
            new Programme("prg-001", "store-001", "Holiday Readiness", "Seasonal rollout: merchandising and staffing.", new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 31), "usr-sm-001", [], SeededAt),
            new Programme("prg-002", "store-002", "Shrink Reduction", "Compliance drive: loss-prevention audits.", new DateOnly(2026, 1, 1), null, "usr-sm-002", [], SeededAt),
        }.ToDictionary(p => p.Id),
        StringComparer.Ordinal);

    public Task<Programme?> GetByIdAsync(string id, CancellationToken cancellationToken)
        => Task.FromResult(_programmes.GetValueOrDefault(id));

    public Task<IReadOnlyList<Programme>> ListByStoreAsync(string storeId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Programme> result = _programmes.Values
            .Where(p => p.StoreId == storeId)
            .OrderBy(p => p.Id, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<bool> NameExistsInStoreAsync(string storeId, string name, CancellationToken cancellationToken)
        => Task.FromResult(_programmes.Values.Any(p =>
            p.StoreId == storeId && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task SaveAsync(Programme programme, CancellationToken cancellationToken)
    {
        _programmes[programme.Id] = programme;
        return Task.CompletedTask;
    }
}
