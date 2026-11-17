using StoreOps.Api.Modules.Reports.Contracts;

namespace StoreOps.Api.Modules.Reports;

internal sealed record Report(
    string Id,
    ReportType Type,
    ReportStatus Status,
    string ScopeId,
    StoreSummaryMetrics? Metrics,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt)
{
    public ReportDto ToDto() => new(Id, Type, Status, ScopeId, Metrics, RequestedAt, CompletedAt);
}
