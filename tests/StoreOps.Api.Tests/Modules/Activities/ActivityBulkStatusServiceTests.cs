using StoreOps.Api.Modules.Activities;
using StoreOps.Api.Modules.Activities.Contracts;
using StoreOps.Api.Modules.Alerts.Contracts;
using StoreOps.Api.Modules.Staff.Contracts;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Tests.Infrastructure;

namespace StoreOps.Api.Tests.Modules.Activities;

/// <summary>
/// Business rules of the shift-handover bulk status update (sprint 1: validation and happy path;
/// sprint 2: per-item failures for store scope, associate permissions and the reopen rule).
/// </summary>
public sealed class ActivityBulkStatusServiceTests
{
    private const string MissingId = "act-missing";

    private static readonly CurrentUser RegionalManager = new("usr-rm-001", "store-001", "region-north", StaffRole.RegionalManager);

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

    [Fact]
    public async Task BulkStatus_AC2_1_MissingAndOtherStoreIds_FailAsNotFound_AndTheOwnStoreActivityIsStillUpdated()
    {
        var a = await CreateAsync("Restock aisle 1");
        var b = await _ctx.Activities.CreateAsync(TestUsers.OtherStoreManager, Request("Store 2 restock"), default);
        _ctx.Time.Now = _ctx.Time.Now.AddMinutes(5);
        _ctx.Events.Clear();

        var result = await BulkAsync(TestUsers.StoreManager, ActivityStatus.Done, b.Id, MissingId, a.Id);

        Assert.Equal((3, 1, 0, 2), (result.Requested, result.Updated, result.Unchanged, result.Failed));
        AssertFailed(result.Results[0], b.Id, ErrorCodes.NotFound);
        AssertFailed(result.Results[1], MissingId, ErrorCodes.NotFound);
        Assert.Equal(new BulkStatusItemResult(a.Id, BulkStatusOutcome.Updated, ActivityStatus.Todo, ActivityStatus.Done, null), result.Results[2]);

        var storedB = await FindAsync(b.Id);
        Assert.Equal((ActivityStatus.Todo, b.UpdatedAt), (storedB.Status, storedB.UpdatedAt));
        Assert.Equal(ActivityStatus.Done, (await FindAsync(a.Id)).Status);
        Assert.Null(await _ctx.Activities.FindByIdAsync(MissingId, default));

        var evt = Assert.Single(_ctx.Events.Of<ActivityStatusChanged>());
        Assert.Equal((a.Id, ActivityStatus.Todo, ActivityStatus.Done), (evt.ActivityId, evt.PreviousStatus, evt.NewStatus));
        Assert.Single(_ctx.Events.Published);
    }

    [Fact]
    public async Task BulkStatus_AC2_2_Associate_UpdatesAssignedAndOwnedActivities_AndIsForbiddenOnOthers()
    {
        var a = await _ctx.Activities.CreateAsync(
            TestUsers.StoreManager, new CreateActivityRequest("Assigned restock", null, null, null, null, null, "usr-as-001", null), default);
        var b = await CreateAsync("Unassigned audit");
        var c = await _ctx.Activities.CreateAsync(TestUsers.Associate, Request("Own facing task"), default);
        _ctx.Time.Now = _ctx.Time.Now.AddMinutes(5);
        _ctx.Events.Clear();

        var result = await BulkAsync(TestUsers.Associate, ActivityStatus.Done, a.Id, b.Id, c.Id);

        Assert.Equal((3, 2, 0, 1), (result.Requested, result.Updated, result.Unchanged, result.Failed));
        Assert.Equal(new BulkStatusItemResult(a.Id, BulkStatusOutcome.Updated, ActivityStatus.Todo, ActivityStatus.Done, null), result.Results[0]);
        AssertFailed(result.Results[1], b.Id, ErrorCodes.Forbidden);
        Assert.Equal(new BulkStatusItemResult(c.Id, BulkStatusOutcome.Updated, ActivityStatus.Todo, ActivityStatus.Done, null), result.Results[2]);

        var storedB = await FindAsync(b.Id);
        Assert.Equal((ActivityStatus.Todo, b.UpdatedAt), (storedB.Status, storedB.UpdatedAt));
        Assert.Equal(ActivityStatus.Done, (await FindAsync(a.Id)).Status);
        Assert.Equal(ActivityStatus.Done, (await FindAsync(c.Id)).Status);

        var events = _ctx.Events.Of<ActivityStatusChanged>().ToList();
        Assert.Equal([a.Id, c.Id], events.Select(e => e.ActivityId));
        Assert.All(events, e => Assert.Equal("usr-as-001", e.ChangedBy));
        Assert.Equal(2, _ctx.Events.Published.Count);
    }

    [Fact]
    public async Task BulkStatus_AC2_3_DepartmentLeadAndRegionalManager_MayUpdateAnyActivityOfTheirStore()
    {
        var a = await CreateAsync("Planogram reset");
        var b = await CreateAsync("Compliance check");
        _ctx.Events.Clear();

        var leadResult = await BulkAsync(TestUsers.DepartmentLead, ActivityStatus.Blocked, a.Id);
        var regionalResult = await BulkAsync(RegionalManager, ActivityStatus.Done, b.Id);

        Assert.Equal((1, 1, 0, 0), (leadResult.Requested, leadResult.Updated, leadResult.Unchanged, leadResult.Failed));
        Assert.Equal((1, 1, 0, 0), (regionalResult.Requested, regionalResult.Updated, regionalResult.Unchanged, regionalResult.Failed));
        Assert.Equal(
            new BulkStatusItemResult(a.Id, BulkStatusOutcome.Updated, ActivityStatus.Todo, ActivityStatus.Blocked, null), Assert.Single(leadResult.Results));
        Assert.Equal(
            new BulkStatusItemResult(b.Id, BulkStatusOutcome.Updated, ActivityStatus.Todo, ActivityStatus.Done, null), Assert.Single(regionalResult.Results));
        Assert.Equal(ActivityStatus.Blocked, (await FindAsync(a.Id)).Status);
        Assert.Equal(ActivityStatus.Done, (await FindAsync(b.Id)).Status);

        var events = _ctx.Events.Of<ActivityStatusChanged>().ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal((a.Id, ActivityStatus.Blocked, "usr-dl-001"), (events[0].ActivityId, events[0].NewStatus, events[0].ChangedBy));
        Assert.Equal((b.Id, ActivityStatus.Done, "usr-rm-001"), (events[1].ActivityId, events[1].NewStatus, events[1].ChangedBy));
        Assert.Equal(2, _ctx.Events.Published.Count);
    }

    [Fact]
    public async Task BulkStatus_AC2_4_DoneActivity_CannotBeBlocked_AndRaisesNoEscalation_WhileBlockedCanBeDone()
    {
        var a = await CreateAsync("Finished restock");
        var b = await CreateAsync("Stuck delivery");
        var c = await CreateAsync("Pending audit");
        await SetStatusAsync(a.Id, ActivityStatus.Done);
        await SetStatusAsync(b.Id, ActivityStatus.Blocked);
        var doneAt = (await FindAsync(a.Id)).UpdatedAt;
        _ctx.Time.Now = _ctx.Time.Now.AddMinutes(5);
        _ctx.Events.Clear();

        var blockResult = await BulkAsync(TestUsers.StoreManager, ActivityStatus.Blocked, a.Id, c.Id);

        Assert.Equal((2, 1, 0, 1), (blockResult.Requested, blockResult.Updated, blockResult.Unchanged, blockResult.Failed));
        AssertFailed(blockResult.Results[0], a.Id, ErrorCodes.BusinessRuleViolation);
        Assert.Equal(
            new BulkStatusItemResult(c.Id, BulkStatusOutcome.Updated, ActivityStatus.Todo, ActivityStatus.Blocked, null), blockResult.Results[1]);
        var storedA = await FindAsync(a.Id);
        Assert.Equal((ActivityStatus.Done, doneAt), (storedA.Status, storedA.UpdatedAt));
        Assert.Equal(ActivityStatus.Blocked, (await FindAsync(c.Id)).Status);
        var blockedEvent = Assert.Single(_ctx.Events.Of<ActivityStatusChanged>());
        Assert.Equal(c.Id, blockedEvent.ActivityId);
        Assert.Single(_ctx.Events.Published);

        // Deliver what was published to Alerts as the real bus would: c escalates, a never does.
        await _ctx.Notifications.EscalateBlockedActivityAsync(blockedEvent, default);
        var alerts = await _ctx.Notifications.ListForUserAsync(TestUsers.StoreManager, default);
        Assert.DoesNotContain(alerts, n => n.SourceActivityId == a.Id);
        Assert.Contains(alerts, n => n.SourceActivityId == c.Id && n.Type == AlertType.Escalation);

        _ctx.Events.Clear();
        var doneResult = await BulkAsync(TestUsers.StoreManager, ActivityStatus.Done, b.Id);

        Assert.Equal(
            new BulkStatusItemResult(b.Id, BulkStatusOutcome.Updated, ActivityStatus.Blocked, ActivityStatus.Done, null), Assert.Single(doneResult.Results));
        Assert.Equal(ActivityStatus.Done, (await FindAsync(b.Id)).Status);
        var doneEvent = Assert.Single(_ctx.Events.Of<ActivityStatusChanged>());
        Assert.Equal((b.Id, ActivityStatus.Blocked, ActivityStatus.Done), (doneEvent.ActivityId, doneEvent.PreviousStatus, doneEvent.NewStatus));
    }

    [Fact]
    public async Task BulkStatus_AC2_5_EveryItemFailing_ReturnsAllFailed_AndChangesNothing()
    {
        var b = await _ctx.Activities.CreateAsync(TestUsers.OtherStoreManager, Request("Store 2 restock"), default);
        _ctx.Time.Now = _ctx.Time.Now.AddMinutes(5);
        _ctx.Events.Clear();

        var result = await BulkAsync(TestUsers.StoreManager, ActivityStatus.Done, MissingId, b.Id);

        Assert.Equal((2, 0, 0, 2), (result.Requested, result.Updated, result.Unchanged, result.Failed));
        AssertFailed(result.Results[0], MissingId, ErrorCodes.NotFound);
        AssertFailed(result.Results[1], b.Id, ErrorCodes.NotFound);
        var storedB = await FindAsync(b.Id);
        Assert.Equal((ActivityStatus.Todo, b.UpdatedAt), (storedB.Status, storedB.UpdatedAt));
        Assert.Empty(_ctx.Events.Published);
    }

    [Fact]
    public async Task BulkStatus_AC2_6_CheckOrder_NotFoundBeatsForbidden_AndForbiddenBeatsUnchanged()
    {
        var a = await CreateAsync("Finished restock");
        await SetStatusAsync(a.Id, ActivityStatus.Done);
        var doneA = await FindAsync(a.Id);
        var b = await _ctx.Activities.CreateAsync(TestUsers.OtherStoreManager, Request("Store 2 restock"), default);
        _ctx.Time.Now = _ctx.Time.Now.AddMinutes(5);
        _ctx.Events.Clear();

        var result = await BulkAsync(TestUsers.Associate, ActivityStatus.Done, a.Id, b.Id);

        Assert.Equal((2, 0, 0, 2), (result.Requested, result.Updated, result.Unchanged, result.Failed));
        AssertFailed(result.Results[0], a.Id, ErrorCodes.Forbidden);
        AssertFailed(result.Results[1], b.Id, ErrorCodes.NotFound);
        var storedA = await FindAsync(a.Id);
        Assert.Equal((ActivityStatus.Done, doneA.UpdatedAt), (storedA.Status, storedA.UpdatedAt));
        var storedB = await FindAsync(b.Id);
        Assert.Equal((ActivityStatus.Todo, b.UpdatedAt), (storedB.Status, storedB.UpdatedAt));
        Assert.Empty(_ctx.Events.Published);
    }

    [Fact]
    public async Task BulkStatus_AC2_6_CheckOrder_ForbiddenBeatsBusinessRule_ForAssociateBlockingAnothersDoneActivity()
    {
        var a = await CreateAsync("Finished restock");
        await SetStatusAsync(a.Id, ActivityStatus.Done);
        var own = await _ctx.Activities.CreateAsync(TestUsers.Associate, Request("Own facing task"), default);
        await _ctx.Activities.UpdateAsync(TestUsers.Associate, own.Id, new UpdateActivityRequest(ActivityStatus.Done, null, null, null), default);
        _ctx.Events.Clear();

        var result = await BulkAsync(TestUsers.Associate, ActivityStatus.Blocked, a.Id, own.Id);

        Assert.Equal((2, 0, 0, 2), (result.Requested, result.Updated, result.Unchanged, result.Failed));
        AssertFailed(result.Results[0], a.Id, ErrorCodes.Forbidden);
        AssertFailed(result.Results[1], own.Id, ErrorCodes.BusinessRuleViolation);
        Assert.Equal(ActivityStatus.Done, (await FindAsync(a.Id)).Status);
        Assert.Equal(ActivityStatus.Done, (await FindAsync(own.Id)).Status);
        Assert.Empty(_ctx.Events.Published);
    }

    private static CreateActivityRequest Request(string title) => new(title, null, null, null, null, null, null, null);

    private static void AssertFailed(BulkStatusItemResult item, string activityId, string code)
    {
        Assert.Equal((activityId, BulkStatusOutcome.Failed), (item.ActivityId, item.Outcome));
        Assert.Null(item.PreviousStatus);
        Assert.Null(item.NewStatus);
        Assert.NotNull(item.Error);
        Assert.Equal(code, item.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(item.Error.Message));
    }

    private Task<BulkStatusUpdateResponse> BulkAsync(CurrentUser user, ActivityStatus target, params string[] activityIds)
        => _ctx.Activities.BulkUpdateStatusAsync(user, new BulkStatusUpdateRequest(target, activityIds), default);

    private Task<ActivityDto> SetStatusAsync(string id, ActivityStatus status)
        => _ctx.Activities.UpdateAsync(TestUsers.StoreManager, id, new UpdateActivityRequest(status, null, null, null), default);

    private Task<ActivityDto> CreateAsync(string title)
        => _ctx.Activities.CreateAsync(TestUsers.StoreManager, Request(title), default);

    private async Task<ActivityDto> FindAsync(string id)
    {
        var activity = await _ctx.Activities.FindByIdAsync(id, default);
        Assert.NotNull(activity);
        return activity;
    }
}
