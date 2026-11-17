using StoreOps.Api.Modules.Activities;
using StoreOps.Api.Modules.Activities.Contracts;
using StoreOps.Api.Modules.Reports;
using StoreOps.Api.Modules.Reports.Contracts;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Tests.Infrastructure;

namespace StoreOps.Api.Tests.Modules.Reports;

public sealed class ReportServiceTests
{
    private readonly ModuleTestContext _ctx = new();

    [Fact]
    public async Task GenerateStoreSummary_AggregatesCompletionAndOverdueByDepartment()
    {
        var yesterday = _ctx.Time.Now.AddDays(-1);
        var done = await CreateAsync("Done", "GROCERY", yesterday);
        await CreateAsync("Overdue grocery", "GROCERY", yesterday);
        var blocked = await CreateAsync("Overdue, no department", null, yesterday);
        await CreateAsync("Not due yet", "BAKERY", _ctx.Time.Now.AddDays(1));
        await SetStatusAsync(done.Id, ActivityStatus.Done);
        await SetStatusAsync(blocked.Id, ActivityStatus.Blocked);

        var report = await _ctx.Reports.GenerateStoreSummaryAsync("store-001", default);

        Assert.Equal((ReportType.StoreSummary, ReportStatus.Ready, "store-001"), (report.Type, report.Status, report.ScopeId));
        var metrics = report.Metrics!;
        Assert.Equal((4, 1, 1, 2), (metrics.TotalActivities, metrics.DoneActivities, metrics.BlockedActivities, metrics.OverdueActivities));
        Assert.Equal(0.25, metrics.CompletionRate);
        Assert.Equal(1, metrics.OverdueByDepartment["GROCERY"]);
        Assert.Equal(1, metrics.OverdueByDepartment[ReportService.UnassignedDepartment]);
        Assert.Equal(report, await _ctx.Reports.GetAsync(report.Id, default));
    }

    [Fact]
    public async Task GenerateStoreSummary_IsReadOnlyTowardsOtherModules()
    {
        var activity = await CreateAsync("Untouched", "GROCERY", null);
        _ctx.Events.Clear();

        await _ctx.Reports.GenerateStoreSummaryAsync("store-001", default);

        // Spec §3.5 read-only reports: no events raised, no activity changed.
        Assert.Empty(_ctx.Events.Published);
        Assert.Equal(activity, await _ctx.Activities.FindByIdAsync(activity.Id, default));
    }

    [Fact]
    public async Task GenerateStoreSummary_ForStoreWithNoActivities_ReturnsZeroRate()
    {
        var report = await _ctx.Reports.GenerateStoreSummaryAsync("store-002", default);

        Assert.Equal(0, report.Metrics!.TotalActivities);
        Assert.Equal(0, report.Metrics.CompletionRate);
    }

    [Fact]
    public async Task GenerateStoreSummary_WithoutStore_ThrowsValidationError()
    {
        await Assert.ThrowsAsync<ValidationError>(() => _ctx.Reports.GenerateStoreSummaryAsync(" ", default));
    }

    [Fact]
    public async Task Get_UnknownReport_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundError>(() => _ctx.Reports.GetAsync("rpt-missing", default));
    }

    private Task<ActivityDto> CreateAsync(string title, string? department, DateTimeOffset? dueAt)
        => _ctx.Activities.CreateAsync(TestUsers.StoreManager, new CreateActivityRequest(title, null, null, null, null, department, null, dueAt), default);

    private Task<ActivityDto> SetStatusAsync(string id, ActivityStatus status)
        => _ctx.Activities.UpdateAsync(TestUsers.StoreManager, id, new UpdateActivityRequest(status, null, null, null), default);
}
