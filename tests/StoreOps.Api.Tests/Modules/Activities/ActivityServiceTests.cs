using StoreOps.Api.Modules.Activities;
using StoreOps.Api.Modules.Activities.Contracts;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Tests.Infrastructure;

namespace StoreOps.Api.Tests.Modules.Activities;

public sealed class ActivityServiceTests
{
    private readonly ModuleTestContext _ctx = new();

    [Fact]
    public async Task Create_AppliesDefaults_ScopesToCallersStore_AndPublishesActivityCreated()
    {
        var created = await _ctx.Activities.CreateAsync(TestUsers.Associate, Request("Reset end caps"), default);

        Assert.Equal(ActivityStatus.Todo, created.Status);
        Assert.Equal(ActivityPriority.Medium, created.Priority);
        Assert.Equal(ActivityCategory.General, created.Category);
        Assert.Equal("store-001", created.StoreId);
        Assert.Equal(TestUsers.Associate.UserId, created.OwnerId);

        var evt = Assert.Single(_ctx.Events.Published);
        var activityCreated = Assert.IsType<ActivityCreated>(evt);
        Assert.Equal(created.Id, activityCreated.ActivityId);
        Assert.Equal("store-001", activityCreated.StoreId);
    }

    [Fact]
    public async Task Create_WithAssignee_PublishesActivityAssigned()
    {
        var created = await _ctx.Activities.CreateAsync(
            TestUsers.StoreManager, Request("Planogram reset", assigneeId: "usr-as-001", programmeId: "prg-001"), default);

        var assigned = Assert.Single(_ctx.Events.Of<ActivityAssigned>());
        Assert.Equal(created.Id, assigned.ActivityId);
        Assert.Null(assigned.PreviousAssigneeId);
        Assert.Equal("usr-as-001", assigned.AssigneeId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Create_WithoutTitle_ThrowsValidationError(string? title)
    {
        var error = await Assert.ThrowsAsync<ValidationError>(
            () => _ctx.Activities.CreateAsync(TestUsers.Associate, Request(title), default));

        Assert.Equal("title", error.Field);
        Assert.Empty(_ctx.Events.Published);
    }

    [Fact]
    public async Task Create_WithProgrammeOfAnotherStore_ThrowsNotFound()
    {
        // prg-002 belongs to store-002; store-001 staff must not see it.
        await Assert.ThrowsAsync<NotFoundError>(
            () => _ctx.Activities.CreateAsync(TestUsers.Associate, Request("Audit", programmeId: "prg-002"), default));
    }

    [Theory]
    [InlineData("usr-missing", typeof(NotFoundError))]
    [InlineData("usr-sm-002", typeof(BusinessRuleError))]
    [InlineData("usr-as-002", typeof(BusinessRuleError))]
    public async Task Create_WithUnassignableAssignee_IsRejected(string assigneeId, Type expected)
    {
        // Unknown user → 404; user of another store or an inactive user → business rule (422).
        var error = await Assert.ThrowsAnyAsync<AppError>(
            () => _ctx.Activities.CreateAsync(TestUsers.StoreManager, Request("Audit", assigneeId: assigneeId), default));

        Assert.IsType(expected, error);
        Assert.Empty(await _ctx.Activities.ListAsync(TestUsers.StoreManager, null, null, default));
    }

    [Fact]
    public async Task List_IsScopedToCallersStore_AndFiltersByProgrammeAndStatus()
    {
        var inProgramme = await _ctx.Activities.CreateAsync(TestUsers.StoreManager, Request("A", programmeId: "prg-001"), default);
        _ctx.Time.Now = _ctx.Time.Now.AddSeconds(1); // lists are ordered by creation time
        var loose = await _ctx.Activities.CreateAsync(TestUsers.StoreManager, Request("B"), default);
        await _ctx.Activities.CreateAsync(TestUsers.OtherStoreManager, Request("Other store"), default);
        await _ctx.Activities.UpdateAsync(TestUsers.StoreManager, loose.Id, new UpdateActivityRequest(ActivityStatus.Blocked, null, null, null), default);

        var all = await _ctx.Activities.ListAsync(TestUsers.StoreManager, null, null, default);
        var byProgramme = await _ctx.Activities.ListAsync(TestUsers.StoreManager, "prg-001", null, default);
        var blocked = await _ctx.Activities.ListAsync(TestUsers.StoreManager, null, ActivityStatus.Blocked, default);

        Assert.Equal([inProgramme.Id, loose.Id], all.Select(a => a.Id));
        Assert.Equal(inProgramme.Id, Assert.Single(byProgramme).Id);
        Assert.Equal(loose.Id, Assert.Single(blocked).Id);
    }

    [Fact]
    public async Task Get_ActivityOfAnotherStore_ThrowsNotFound()
    {
        var foreign = await _ctx.Activities.CreateAsync(TestUsers.OtherStoreManager, Request("Other store"), default);

        await Assert.ThrowsAsync<NotFoundError>(() => _ctx.Activities.GetAsync(TestUsers.StoreManager, foreign.Id, default));
    }

    [Fact]
    public async Task Update_ChangesFields_AndPublishesStatusChangeWithBothStatuses()
    {
        var activity = await _ctx.Activities.CreateAsync(TestUsers.StoreManager, Request("Compliance check"), default);
        _ctx.Events.Clear();
        _ctx.Time.Now = _ctx.Time.Now.AddMinutes(5);

        var updated = await _ctx.Activities.UpdateAsync(
            TestUsers.DepartmentLead,
            activity.Id,
            new UpdateActivityRequest(ActivityStatus.InProgress, ActivityPriority.Critical, ActivityCategory.Compliance, null),
            default);

        Assert.Equal(ActivityStatus.InProgress, updated.Status);
        Assert.Equal(ActivityPriority.Critical, updated.Priority);
        Assert.Equal(ActivityCategory.Compliance, updated.Category);
        Assert.Equal(_ctx.Time.Now, updated.UpdatedAt);

        var changed = Assert.Single(_ctx.Events.Of<ActivityStatusChanged>());
        Assert.Equal(ActivityStatus.Todo, changed.PreviousStatus);
        Assert.Equal(ActivityStatus.InProgress, changed.NewStatus);
        Assert.Equal(ActivityPriority.Critical, changed.Priority);
        Assert.Equal(TestUsers.DepartmentLead.UserId, changed.ChangedBy);
    }

    [Fact]
    public async Task Update_WithoutStatusChange_PublishesNoStatusEvent()
    {
        var activity = await _ctx.Activities.CreateAsync(TestUsers.StoreManager, Request("Restock"), default);
        _ctx.Events.Clear();

        await _ctx.Activities.UpdateAsync(TestUsers.StoreManager, activity.Id, new UpdateActivityRequest(null, ActivityPriority.High, null, null), default);

        Assert.Empty(_ctx.Events.Published);
    }

    [Fact]
    public async Task Update_Reassignment_PublishesActivityAssignedWithPreviousAssignee()
    {
        var activity = await _ctx.Activities.CreateAsync(TestUsers.StoreManager, Request("Restock", assigneeId: "usr-as-001"), default);
        _ctx.Events.Clear();

        await _ctx.Activities.UpdateAsync(TestUsers.StoreManager, activity.Id, new UpdateActivityRequest(null, null, null, "usr-dl-001"), default);

        var assigned = Assert.Single(_ctx.Events.Of<ActivityAssigned>());
        Assert.Equal("usr-as-001", assigned.PreviousAssigneeId);
        Assert.Equal("usr-dl-001", assigned.AssigneeId);
    }

    [Fact]
    public async Task Update_WithNoFields_ThrowsValidationError()
    {
        var activity = await _ctx.Activities.CreateAsync(TestUsers.StoreManager, Request("Restock"), default);

        await Assert.ThrowsAsync<ValidationError>(
            () => _ctx.Activities.UpdateAsync(TestUsers.StoreManager, activity.Id, new UpdateActivityRequest(null, null, null, null), default));
    }

    [Fact]
    public async Task Update_ReopeningADoneActivity_ThrowsBusinessRuleError_AndKeepsItDone()
    {
        var activity = await _ctx.Activities.CreateAsync(TestUsers.StoreManager, Request("Restock"), default);
        await _ctx.Activities.UpdateAsync(TestUsers.StoreManager, activity.Id, new UpdateActivityRequest(ActivityStatus.Done, null, null, null), default);

        await Assert.ThrowsAsync<BusinessRuleError>(() => _ctx.Activities.UpdateAsync(
            TestUsers.StoreManager, activity.Id, new UpdateActivityRequest(ActivityStatus.InProgress, null, null, null), default));

        Assert.Equal(ActivityStatus.Done, (await _ctx.Activities.GetAsync(TestUsers.StoreManager, activity.Id, default)).Status);
    }

    [Fact]
    public async Task Delete_ByOwner_RemovesActivity_AndPublishesActivityDeleted()
    {
        var activity = await _ctx.Activities.CreateAsync(TestUsers.Associate, Request("Mine"), default);

        await _ctx.Activities.DeleteAsync(TestUsers.Associate, activity.Id, default);

        await Assert.ThrowsAsync<NotFoundError>(() => _ctx.Activities.GetAsync(TestUsers.Associate, activity.Id, default));
        Assert.Equal(TestUsers.Associate.UserId, Assert.Single(_ctx.Events.Of<ActivityDeleted>()).DeletedBy);
    }

    [Fact]
    public async Task Delete_ByStoreManagerWhoIsNotOwner_IsAllowed()
    {
        var activity = await _ctx.Activities.CreateAsync(TestUsers.Associate, Request("Theirs"), default);

        await _ctx.Activities.DeleteAsync(TestUsers.StoreManager, activity.Id, default);

        Assert.Empty(await _ctx.Activities.ListAsync(TestUsers.StoreManager, null, null, default));
    }

    [Fact]
    public async Task Delete_ByNonOwnerWhoIsNotStoreManager_ThrowsForbidden_AndKeepsActivity()
    {
        var activity = await _ctx.Activities.CreateAsync(TestUsers.Associate, Request("Theirs"), default);
        _ctx.Events.Clear();

        await Assert.ThrowsAsync<ForbiddenError>(() => _ctx.Activities.DeleteAsync(TestUsers.DepartmentLead, activity.Id, default));

        Assert.NotNull(await _ctx.Activities.FindByIdAsync(activity.Id, default));
        Assert.Empty(_ctx.Events.Published);
    }

    [Fact]
    public async Task Delete_ByStoreManagerOfAnotherStore_ThrowsNotFound()
    {
        var activity = await _ctx.Activities.CreateAsync(TestUsers.Associate, Request("Theirs"), default);

        await Assert.ThrowsAsync<NotFoundError>(() => _ctx.Activities.DeleteAsync(TestUsers.OtherStoreManager, activity.Id, default));
    }

    [Fact]
    public async Task Queries_ExposeActivitiesToOtherModulesReadOnly()
    {
        var activity = await _ctx.Activities.CreateAsync(TestUsers.Associate, Request("Visible"), default);

        Assert.Equal(activity.Id, (await _ctx.Activities.FindByIdAsync(activity.Id, default))?.Id);
        Assert.Equal(activity.Id, Assert.Single(await _ctx.Activities.ListByStoreAsync("store-001", default)).Id);
        Assert.Empty(await _ctx.Activities.ListByStoreAsync("store-002", default));
    }

    private static CreateActivityRequest Request(string? title, string? assigneeId = null, string? programmeId = null)
        => new(title, null, programmeId, null, null, null, assigneeId, null);
}
