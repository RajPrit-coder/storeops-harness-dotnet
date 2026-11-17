namespace StoreOps.Api.Modules.Reports.Contracts;

// Public surface of the Reports module (spec §3.3 Report, ReportType, ReportStatus).
// Reports is read-only: it aggregates other modules' data through their query contracts and never
// writes to them.
public enum ReportType
{
    StoreSummary,
    RegionalRollup,
    DepartmentPerformance,
}

public enum ReportStatus
{
    Pending,
    Ready,
    Failed,
}

public sealed record ReportDto(
    string Id,
    ReportType Type,
    ReportStatus Status,
    string ScopeId,
    StoreSummaryMetrics? Metrics,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt);

public sealed record StoreSummaryMetrics(
    int TotalActivities,
    int DoneActivities,
    int BlockedActivities,
    int OverdueActivities,
    double CompletionRate,
    IReadOnlyDictionary<string, int> OverdueByDepartment);
