using StoreOps.Api.Modules.Activities;
using StoreOps.Api.Modules.Activities.Contracts;
using StoreOps.Api.Modules.Alerts.Contracts;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Tests.Infrastructure;

namespace StoreOps.Api.Tests.Modules.Activities;

/// <summary>Business rules of the shift-handover bulk status update (sprint 1: validation and happy path).</summary>
public sealed class ActivityBulkStatusServiceTests
{
    private readonly ModuleTestContext _ctx = new();

    public static TheoryData<ActivityStatus?> NonTerminalTargets => new() { ActivityStatus.InProgress, ActivityStatus.Todo, null };

    [Fact]
    public async Task BulkStatus_AC1_1_TwoTodoActivities_AreBothDone_InRequestOrder_WithOneEventEach()
    {
        var a = await CreateAsync("Restock aisle 1");
        var b = await CreateAsync("Restock aisle 2");
        _ctx.Time.Now = _ctx.Time.Now.AddMinutes(5);
        _ctx.Events.Clear();

        var result = await _ctx.Activities.BulkUpdateStatusAsync(
            TestUsers.StoreManager, new BulkStatusUpdateRequest(ActivityStatus.Done, [a.Id, b.Id]), default);

        Assert.Equal(ActivityStatus.Done, result.Status);
        Assert.Equal((2, 2, 0, 0), (result.Requested, result.Updated, result.Unchanged, result.Failed));
        Assert.Equal([a.Id, b.Id], result.Results.Select(r => r.ActivityId));
        Assert.All(result.Results, r =>
        {
            Assert.Equal(BulkStatusOutcome.Updated, r.Outcome);
            Assert.Equal(ActivityStatus.Todo, r.PreviousStatus);
            Assert.Equal(ActivityStatus.Done, r.NewStatus);
            Assert.Null(r.Error);
        });

        foreach (var id in new[] { a.Id, b.Id })
        {
            var stored = await FindAsync(id);
            Assert.Equal(ActivityStatus.Done, stored.Status);
            Assert.True(stored.UpdatedAt > stored.CreatedAt);
        }

        var events = _ctx.Events.Of<ActivityStatusChanged>().ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal(2, _ctx.Events.Published.Count);
        Assert.Equal([a.Id, b.Id], events.Select(e => e.ActivityId));
        Assert.All(events, e =>
        {
            Assert.Equal((ActivityStatus.Todo, ActivityStatus.Done), (e.PreviousStatus, e.NewStatus));
            Assert.Equal("usr-sm-001", e.ChangedBy);
            Assert.Equal("store-001", e.StoreId);
        });
    }

    [Fact]
    public async Task BulkStatus_AC1_2_DepartmentLeadBlocksActivity_PublishesBlockedEvent_ThatRaisesEscalation()
    {
        var a = await CreateAsync("Planogram reset");
        _ctx.Events.Clear();

        var result = await _ctx.Activities.BulkUpdateStatusAsync(
            TestUsers.DepartmentLead, new BulkStatusUpdateRequest(ActivityStatus.Blocked, [a.Id]), default);

        var item = Assert.Single(result.Results);
        Assert.Equal((BulkStatusOutcome.Updated, ActivityStatus.Todo, ActivityStatus.Blocked), (item.Outcome, item.PreviousStatus, item.NewStatus));
        Assert.Equal(ActivityStatus.Blocked, (await FindAsync(a.Id)).Status);

        var evt = Assert.Single(_ctx.Events.Of<ActivityStatusChanged>());
        Assert.Equal((a.Id, ActivityStatus.Blocked, "usr-dl-001"), (evt.ActivityId, evt.NewStatus, evt.ChangedBy));

        // The bus is a recorder here: hand the event to Alerts as the real bus would, to prove it carries
        // everything the existing escalation needs.
        await _ctx.Notifications.EscalateBlockedActivityAsync(evt, default);
        var notification = Assert.Single(await _ctx.Notifications.ListForUserAsync(TestUsers.StoreManager, default));
        Assert.Equal((AlertType.Escalation, NotificationChannel.InApp, a.Id), (notification.Type, notification.Channel, notification.SourceActivityId));
    }

    [Theory]
    [MemberData(nameof(NonTerminalTargets))]
    public async Task BulkStatus_AC1_3_TargetOtherThanDoneOrBlocked_IsRejectedOnStatus_AndNothingChanges(ActivityStatus? target)
    {
        var a = await CreateAsync("Audit freezer");
        _ctx.Time.Now = _ctx.Time.Now.AddMinutes(5);
        _ctx.Events.Clear();

        var error = await Assert.ThrowsAsync<ValidationError>(
            () => _ctx.Activities.BulkUpdateStatusAsync(TestUsers.StoreManager, new BulkStatusUpdateRequest(target, [a.Id]), default));

        Assert.Equal("status", error.Field);
        var stored = await FindAsync(a.Id);
        Assert.Equal((ActivityStatus.Todo, a.UpdatedAt), (stored.Status, stored.UpdatedAt));
        Assert.Empty(_ctx.Events.Published);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("blank")]
    [InlineData("null-entry")]
    [InlineData("duplicate")]
    [InlineData("too-many")]
    public async Task BulkStatus_AC1_4_InvalidActivityIds_RejectsWholeRequest_AndValidFirstIdIsNotUpdated(string shape)
    {
        var a = await CreateAsync("Restock dairy");
        _ctx.Events.Clear();
        IReadOnlyList<string?>? ids = shape switch
        {
            "missing" => null,
            "empty" => [],
            "blank" => [a.Id, "  "],
            "null-entry" => [a.Id, null],
            "duplicate" => [a.Id, a.Id],
            _ => [a.Id, .. Enumerable.Range(1, ActivityService.MaxBulkActivityIds).Select(i => $"act-other-{i}")],
        };

        var error = await Assert.ThrowsAsync<ValidationError>(
            () => _ctx.Activities.BulkUpdateStatusAsync(TestUsers.StoreManager, new BulkStatusUpdateRequest(ActivityStatus.Done, ids), default));

        Assert.Equal("activityIds", error.Field);
        Assert.Equal(ActivityStatus.Todo, (await FindAsync(a.Id)).Status);
        Assert.Empty(_ctx.Events.Published);
    }

    [Fact]
    public async Task BulkStatus_AC1_4_ExactlyFiftyDistinctIds_AreAllUpdated()
    {
        var ids = new List<string?>();
        for (var i = 0; i < ActivityService.MaxBulkActivityIds; i++)
        {
            ids.Add((await CreateAsync($"Handover item {i}")).Id);
        }

        _ctx.Events.Clear();

        var result = await _ctx.Activities.BulkUpdateStatusAsync(
            TestUsers.StoreManager, new BulkStatusUpdateRequest(ActivityStatus.Done, ids), default);

        Assert.Equal((50, 50, 0, 0), (result.Requested, result.Updated, result.Unchanged, result.Failed));
        Assert.Equal(50, _ctx.Events.Of<ActivityStatusChanged>().Count());
        foreach (var id in ids)
        {
            Assert.Equal(ActivityStatus.Done, (await FindAsync(id!)).Status);
        }
    }

    [Fact]
    public async Task BulkStatus_AC1_5_ActivityAlreadyInTarget_IsUnchanged_NotTouched_AndGetsNoEvent()
    {
        var a = await CreateAsync("Blocked delivery");
        var b = await CreateAsync("Restock bakery");
        await _ctx.Activities.UpdateAsync(TestUsers.StoreManager, a.Id, new UpdateActivityRequest(ActivityStatus.Blocked, null, null, null), default);
        var blockedAt = (await FindAsync(a.Id)).UpdatedAt;
        _ctx.Time.Now = _ctx.Time.Now.AddMinutes(5);
        _ctx.Events.Clear();

        var result = await _ctx.Activities.BulkUpdateStatusAsync(
            TestUsers.StoreManager, new BulkStatusUpdateRequest(ActivityStatus.Blocked, [a.Id, b.Id]), default);

        Assert.Equal((2, 1, 1, 0), (result.Requested, result.Updated, result.Unchanged, result.Failed));
        Assert.Equal(
            new BulkStatusItemResult(a.Id, BulkStatusOutcome.Unchanged, ActivityStatus.Blocked, ActivityStatus.Blocked, null), result.Results[0]);
        Assert.Equal(
            new BulkStatusItemResult(b.Id, BulkStatusOutcome.Updated, ActivityStatus.Todo, ActivityStatus.Blocked, null), result.Results[1]);

        var storedA = await FindAsync(a.Id);
        Assert.Equal((ActivityStatus.Blocked, blockedAt), (storedA.Status, storedA.UpdatedAt));
        Assert.Equal(ActivityStatus.Blocked, (await FindAsync(b.Id)).Status);
        var evt = Assert.Single(_ctx.Events.Of<ActivityStatusChanged>());
        Assert.Equal(b.Id, evt.ActivityId);
        Assert.Single(_ctx.Events.Published);
    }

    private static CreateActivityRequest Request(string title) => new(title, null, null, null, null, null, null, null);

    private Task<ActivityDto> CreateAsync(string title)
        => _ctx.Activities.CreateAsync(TestUsers.StoreManager, Request(title), default);

    private async Task<ActivityDto> FindAsync(string id)
    {
        var activity = await _ctx.Activities.FindByIdAsync(id, default);
        Assert.NotNull(activity);
        return activity;
    }
}
