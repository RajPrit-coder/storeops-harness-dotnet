using StoreOps.Api.Modules.Activities.Contracts;
using StoreOps.Api.Modules.Reports.Contracts;
using StoreOps.Api.Shared;
using StoreOps.Api.Shared.Errors;

namespace StoreOps.Api.Modules.Reports;

/// <summary>
/// Stub report generation. Reads activities through <see cref="IActivityQueries"/> (read-only) and
/// stores the result in its own repository. The base API exposes no report endpoint (spec §3.6); the
/// regional rollup feature (spec §3.4) is expected to add one.
/// </summary>
internal sealed class ReportService(IReportRepository repository, IActivityQueries activityQueries, TimeProvider timeProvider)
{
    public const string UnassignedDepartment = "UNASSIGNED";

    public async Task<ReportDto> GetAsync(string reportId, CancellationToken cancellationToken)
        => (await repository.GetByIdAsync(reportId, cancellationToken))?.ToDto()
            ?? throw new NotFoundError("Report", reportId);

    public async Task<ReportDto> GenerateStoreSummaryAsync(string storeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId))
        {
            throw new ValidationError("storeId is required.", "storeId");
        }

        var requestedAt = timeProvider.GetUtcNow();
        var report = new Report(Ids.New("rpt"), ReportType.StoreSummary, ReportStatus.Pending, storeId, null, requestedAt, null);
        await repository.SaveAsync(report, cancellationToken);

        var activities = await activityQueries.ListByStoreAsync(storeId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var overdue = activities.Where(a => a.Status != ActivityStatus.Done && a.DueAt is { } due && due < now).ToList();
        var done = activities.Count(a => a.Status == ActivityStatus.Done);

        var metrics = new StoreSummaryMetrics(
            activities.Count,
            done,
            activities.Count(a => a.Status == ActivityStatus.Blocked),
            overdue.Count,
            activities.Count == 0 ? 0 : Math.Round((double)done / activities.Count, 4),
            overdue
                .GroupBy(a => a.Department ?? UnassignedDepartment, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal));

        var ready = report with { Status = ReportStatus.Ready, Metrics = metrics, CompletedAt = now };
        await repository.SaveAsync(ready, cancellationToken);
        return ready.ToDto();
    }
}
