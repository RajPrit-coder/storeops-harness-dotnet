using System.Collections.Concurrent;

namespace StoreOps.Api.Modules.Reports;

/// <summary>Stores Report records only. Reports writes nothing but its own reports.</summary>
internal interface IReportRepository
{
    Task<Report?> GetByIdAsync(string id, CancellationToken cancellationToken);

    Task SaveAsync(Report report, CancellationToken cancellationToken);
}

internal sealed class InMemoryReportRepository : IReportRepository
{
    private readonly ConcurrentDictionary<string, Report> _reports = new(StringComparer.Ordinal);

    public Task<Report?> GetByIdAsync(string id, CancellationToken cancellationToken)
        => Task.FromResult(_reports.GetValueOrDefault(id));

    public Task SaveAsync(Report report, CancellationToken cancellationToken)
    {
        _reports[report.Id] = report;
        return Task.CompletedTask;
    }
}
