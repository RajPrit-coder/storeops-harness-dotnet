using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StoreOps.Api.Modules.Activities;
using StoreOps.Api.Modules.Activities.Contracts;
using StoreOps.Api.Modules.Alerts.Contracts;
using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Shared.Events;
using StoreOps.Api.Tests.Infrastructure;

namespace StoreOps.Api.Tests.Modules.Activities;

/// <summary>
/// <c>PATCH /api/activities/bulk-status</c> over HTTP: routing (the literal route wins over <c>{id}</c>),
/// wire format, validation envelope, auth, the events seen by a recording subscriber on the real bus, and
/// <c>GET /api/activities/{id}/audit</c> with every entry the real audit repository received.
/// </summary>
public sealed class ActivityBulkStatusRoutesTests : IDisposable
{
    private const string BulkUrl = "/api/activities/bulk-status";
    private const string MissingId = "act-missing";

    private readonly StatusChangedRecorder _recorder = new();
    private readonly RecordingActivityAuditRepository _audit = new();
    private readonly TestApi _api;
    private readonly HttpClient _manager;

    public ActivityBulkStatusRoutesTests()
    {
        _api = new TestApi(services =>
        {
            services.AddSingleton<IEventSubscriber<ActivityStatusChanged>>(_recorder);
            services.AddSingleton<IActivityAuditRepository>(_audit);
        });
        _manager = _api.As(StaffSeedData.StoreManagerToken);
    }

    public static TheoryData<string, string?> InvalidStatusBodies => new()
    {
        { """{ "status": "IN_PROGRESS", "activityIds": ["{id}"] }""", "status" },
        { """{ "status": "TODO", "activityIds": ["{id}"] }""", "status" },
        { """{ "activityIds": ["{id}"] }""", "status" },
        { """{ "status": "done", "activityIds": ["{id}"] }""", null },
    };

    public static TheoryData<string> InvalidActivityIdLists => new()
    {
        "missing",
        "empty",
        "blank",
        "duplicate",
        "fifty-one",
    };

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task BulkStatus_AC1_1_TwoActivitiesMarkedDone_Returns200PerItemResults_AndBothAreDone()
    {
        var a = await CreateAsync("Restock aisle 1");
        var b = await CreateAsync("Restock aisle 2");
        _recorder.Clear();

        using var response = await _manager.PatchJsonAsync(BulkUrl, new { status = "DONE", activityIds = new[] { a.Id, b.Id } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("DONE", root.GetProperty("status").GetString());
        Assert.Equal(
            (2, 2, 0, 0),
            (root.GetProperty("requested").GetInt32(), root.GetProperty("updated").GetInt32(), root.GetProperty("unchanged").GetInt32(), root.GetProperty("failed").GetInt32()));
        var results = root.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal([a.Id, b.Id], results.Select(r => r.GetProperty("activityId").GetString()));
        Assert.All(results, r =>
        {
            Assert.Equal("UPDATED", r.GetProperty("outcome").GetString());
            Assert.Equal("TODO", r.GetProperty("previousStatus").GetString());
            Assert.Equal("DONE", r.GetProperty("newStatus").GetString());
            Assert.Equal(JsonValueKind.Null, r.GetProperty("error").ValueKind);
        });

        foreach (var id in new[] { a.Id, b.Id })
        {
            var stored = await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{id}");
            Assert.Equal(ActivityStatus.Done, stored.Status);
            Assert.True(stored.UpdatedAt >= stored.CreatedAt);
        }

        var events = _recorder.Events;
        Assert.Equal(2, events.Count);
        Assert.Equal([a.Id, b.Id], events.Select(e => e.ActivityId));
        Assert.All(events, e =>
        {
            Assert.Equal((ActivityStatus.Todo, ActivityStatus.Done), (e.PreviousStatus, e.NewStatus));
            Assert.Equal(("usr-sm-001", "store-001"), (e.ChangedBy, e.StoreId));
        });
    }

    [Fact]
    public async Task BulkStatus_AC1_2_DepartmentLeadBlocks_RaisesExactlyOneEscalationForTheStoreManager()
    {
        var a = await CreateAsync("Planogram reset");
        var before = await _manager.GetJsonAsync<List<NotificationDto>>("/api/alerts");
        Assert.DoesNotContain(before, n => n.SourceActivityId == a.Id);
        _recorder.Clear();

        using var response = await _api.As(StaffSeedData.DepartmentLeadToken)
            .PatchJsonAsync(BulkUrl, new { status = "BLOCKED", activityIds = new[] { a.Id } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = json.RootElement.GetProperty("results")[0];
        Assert.Equal("UPDATED", item.GetProperty("outcome").GetString());
        Assert.Equal("BLOCKED", item.GetProperty("newStatus").GetString());
        Assert.Equal(ActivityStatus.Blocked, (await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}")).Status);

        var evt = Assert.Single(_recorder.Events);
        Assert.Equal((a.Id, ActivityStatus.Blocked, "usr-dl-001"), (evt.ActivityId, evt.NewStatus, evt.ChangedBy));

        var alerts = (await _manager.GetJsonAsync<List<NotificationDto>>("/api/alerts")).Where(n => n.SourceActivityId == a.Id);
        var escalation = Assert.Single(alerts);
        Assert.Equal((AlertType.Escalation, NotificationChannel.InApp), (escalation.Type, escalation.Channel));
    }

    [Theory]
    [MemberData(nameof(InvalidStatusBodies))]
    public async Task BulkStatus_AC1_3_TargetNotDoneOrBlocked_Returns400_AndActivityIsUnchanged(string bodyTemplate, string? field)
    {
        var a = await CreateAsync("Audit freezer");
        _recorder.Clear();
        using var content = new StringContent(bodyTemplate.Replace("{id}", a.Id, StringComparison.Ordinal), Encoding.UTF8, "application/json");

        using var response = await _manager.PatchAsync(new Uri(BulkUrl, UriKind.Relative), content);

        await response.AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed, field);
        var stored = await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}");
        Assert.Equal((ActivityStatus.Todo, a.UpdatedAt), (stored.Status, stored.UpdatedAt));
        Assert.Empty(_recorder.Events);
    }

    [Theory]
    [MemberData(nameof(InvalidActivityIdLists))]
    public async Task BulkStatus_AC1_4_InvalidActivityIds_Returns400OnActivityIds_AndFirstValidIdIsNotUpdated(string shape)
    {
        var a = await CreateAsync("Restock dairy");
        _recorder.Clear();
        object body = shape switch
        {
            "missing" => new { status = "DONE" },
            "empty" => new { status = "DONE", activityIds = Array.Empty<string>() },
            "blank" => new { status = "DONE", activityIds = new[] { a.Id, "  " } },
            "duplicate" => new { status = "DONE", activityIds = new[] { a.Id, a.Id } },
            _ => new { status = "DONE", activityIds = new[] { a.Id }.Concat(Enumerable.Range(1, 50).Select(i => $"act-other-{i}")).ToArray() },
        };

        using var response = await _manager.PatchJsonAsync(BulkUrl, body);

        await response.AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed, field: "activityIds");
        Assert.Equal(ActivityStatus.Todo, (await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}")).Status);
        Assert.Empty(_recorder.Events);
    }

    [Fact]
    public async Task BulkStatus_AC1_4_FiftyDistinctExistingIds_Returns200_WithFiftyUpdated()
    {
        var ids = new List<string>();
        for (var i = 0; i < 50; i++)
        {
            ids.Add((await CreateAsync($"Handover item {i}")).Id);
        }

        _recorder.Clear();

        using var response = await _manager.PatchJsonAsync(BulkUrl, new { status = "DONE", activityIds = ids });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(50, json.RootElement.GetProperty("updated").GetInt32());
        Assert.Equal(50, _recorder.Events.Count);
        var done = await _manager.GetJsonAsync<List<ActivityDto>>("/api/activities?status=DONE");
        Assert.Equal(ids.Order(StringComparer.Ordinal), done.Select(d => d.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task BulkStatus_AC1_5_AlreadyBlockedActivity_IsUnchanged_AndOnlyTheOtherIsUpdated()
    {
        var a = await CreateAsync("Blocked delivery");
        var b = await CreateAsync("Restock bakery");
        (await _manager.PatchJsonAsync($"/api/activities/{a.Id}", new { status = "BLOCKED" })).Dispose();
        var blockedAt = (await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}")).UpdatedAt;
        _recorder.Clear();

        using var response = await _manager.PatchJsonAsync(BulkUrl, new { status = "BLOCKED", activityIds = new[] { a.Id, b.Id } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(
            (2, 1, 1, 0),
            (root.GetProperty("requested").GetInt32(), root.GetProperty("updated").GetInt32(), root.GetProperty("unchanged").GetInt32(), root.GetProperty("failed").GetInt32()));
        AssertItem(root.GetProperty("results")[0], a.Id, "UNCHANGED", "BLOCKED", "BLOCKED");
        AssertItem(root.GetProperty("results")[1], b.Id, "UPDATED", "TODO", "BLOCKED");

        var storedA = await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}");
        Assert.Equal((ActivityStatus.Blocked, blockedAt), (storedA.Status, storedA.UpdatedAt));
        Assert.Equal(ActivityStatus.Blocked, (await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{b.Id}")).Status);
        Assert.Equal(b.Id, Assert.Single(_recorder.Events).ActivityId);
    }

    [Fact]
    public async Task BulkStatus_AC1_6_WithoutBearerToken_Returns401_AndActivityIsUnchanged()
    {
        var a = await CreateAsync("Restock frozen");
        _recorder.Clear();

        using var response = await _api.Anonymous.PatchJsonAsync(BulkUrl, new { status = "DONE", activityIds = new[] { a.Id } });

        await response.AssertErrorAsync(HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        Assert.Equal(ActivityStatus.Todo, (await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}")).Status);
        Assert.Empty(_recorder.Events);
    }

    [Fact]
    public async Task BulkStatus_AC2_1_MissingAndOtherStoreIds_FailAsNotFound_Returns200_AndOwnStoreActivityIsDone()
    {
        var otherStore = _api.As(StaffSeedData.OtherStoreManagerToken);
        var a = await CreateAsync("Restock aisle 1");
        var b = await CreateAsync("Store 2 restock", otherStore);
        _recorder.Clear();

        using var response = await _manager.PatchJsonAsync(BulkUrl, new { status = "DONE", activityIds = new[] { b.Id, MissingId, a.Id } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        AssertCounts(root, requested: 3, updated: 1, unchanged: 0, failed: 2);
        AssertFailedItem(root.GetProperty("results")[0], b.Id, ErrorCodes.NotFound);
        AssertFailedItem(root.GetProperty("results")[1], MissingId, ErrorCodes.NotFound);
        AssertItem(root.GetProperty("results")[2], a.Id, "UPDATED", "TODO", "DONE");

        var storedB = await otherStore.GetJsonAsync<ActivityDto>($"/api/activities/{b.Id}");
        Assert.Equal((ActivityStatus.Todo, b.UpdatedAt), (storedB.Status, storedB.UpdatedAt));
        Assert.Equal(ActivityStatus.Done, (await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}")).Status);
        Assert.Equal(a.Id, Assert.Single(_recorder.Events).ActivityId);
    }

    [Fact]
    public async Task BulkStatus_AC2_2_Associate_UpdatesAssignedAndOwned_AndGetsForbiddenItemForOthers()
    {
        var associate = _api.As(StaffSeedData.AssociateToken);
        var a = await CreateAsync("Assigned restock", assigneeId: "usr-as-001");
        var b = await CreateAsync("Unassigned audit");
        var c = await CreateAsync("Own facing task", associate);
        _recorder.Clear();

        using var response = await associate.PatchJsonAsync(BulkUrl, new { status = "DONE", activityIds = new[] { a.Id, b.Id, c.Id } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        AssertCounts(root, requested: 3, updated: 2, unchanged: 0, failed: 1);
        AssertItem(root.GetProperty("results")[0], a.Id, "UPDATED", "TODO", "DONE");
        AssertFailedItem(root.GetProperty("results")[1], b.Id, ErrorCodes.Forbidden);
        AssertItem(root.GetProperty("results")[2], c.Id, "UPDATED", "TODO", "DONE");

        var storedB = await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{b.Id}");
        Assert.Equal((ActivityStatus.Todo, b.UpdatedAt), (storedB.Status, storedB.UpdatedAt));
        Assert.Equal(ActivityStatus.Done, (await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}")).Status);
        Assert.Equal(ActivityStatus.Done, (await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{c.Id}")).Status);

        var events = _recorder.Events;
        Assert.Equal([a.Id, c.Id], events.Select(e => e.ActivityId));
        Assert.All(events, e => Assert.Equal("usr-as-001", e.ChangedBy));
    }

    [Fact]
    public async Task BulkStatus_AC2_3_DepartmentLeadAndRegionalManager_UpdateUnassignedActivitiesOfTheirStore()
    {
        var a = await CreateAsync("Planogram reset");
        var b = await CreateAsync("Compliance check");
        _recorder.Clear();

        using var leadResponse = await _api.As(StaffSeedData.DepartmentLeadToken)
            .PatchJsonAsync(BulkUrl, new { status = "BLOCKED", activityIds = new[] { a.Id } });
        using var regionalResponse = await _api.As(StaffSeedData.RegionalManagerToken)
            .PatchJsonAsync(BulkUrl, new { status = "DONE", activityIds = new[] { b.Id } });

        foreach (var (response, id, newStatus) in new[] { (leadResponse, a.Id, "BLOCKED"), (regionalResponse, b.Id, "DONE") })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            AssertCounts(json.RootElement, requested: 1, updated: 1, unchanged: 0, failed: 0);
            AssertItem(json.RootElement.GetProperty("results")[0], id, "UPDATED", "TODO", newStatus);
        }

        Assert.Equal(ActivityStatus.Blocked, (await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}")).Status);
        Assert.Equal(ActivityStatus.Done, (await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{b.Id}")).Status);

        var events = _recorder.Events;
        Assert.Equal(2, events.Count);
        Assert.Equal((a.Id, "usr-dl-001"), (events[0].ActivityId, events[0].ChangedBy));
        Assert.Equal((b.Id, "usr-rm-001"), (events[1].ActivityId, events[1].ChangedBy));
    }

    [Fact]
    public async Task BulkStatus_AC2_4_DoneActivityToBlocked_IsBusinessRuleFailure_WithoutEscalation_AndBlockedToDoneIsUpdated()
    {
        var a = await CreateAsync("Finished restock");
        var b = await CreateAsync("Stuck delivery");
        var c = await CreateAsync("Pending audit");
        (await _manager.PatchJsonAsync($"/api/activities/{a.Id}", new { status = "DONE" })).Dispose();
        (await _manager.PatchJsonAsync($"/api/activities/{b.Id}", new { status = "BLOCKED" })).Dispose();
        var doneA = await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}");
        Assert.Equal(ActivityStatus.Done, doneA.Status);
        _recorder.Clear();

        using (var response = await _manager.PatchJsonAsync(BulkUrl, new { status = "BLOCKED", activityIds = new[] { a.Id, c.Id } }))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            AssertCounts(root, requested: 2, updated: 1, unchanged: 0, failed: 1);
            AssertFailedItem(root.GetProperty("results")[0], a.Id, ErrorCodes.BusinessRuleViolation);
            AssertItem(root.GetProperty("results")[1], c.Id, "UPDATED", "TODO", "BLOCKED");
        }

        var storedA = await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}");
        Assert.Equal((ActivityStatus.Done, doneA.UpdatedAt), (storedA.Status, storedA.UpdatedAt));
        Assert.Equal(c.Id, Assert.Single(_recorder.Events).ActivityId);
        var alerts = await _manager.GetJsonAsync<List<NotificationDto>>("/api/alerts");
        Assert.DoesNotContain(alerts, n => n.SourceActivityId == a.Id);

        _recorder.Clear();
        using var doneResponse = await _manager.PatchJsonAsync(BulkUrl, new { status = "DONE", activityIds = new[] { b.Id } });

        Assert.Equal(HttpStatusCode.OK, doneResponse.StatusCode);
        using var doneJson = JsonDocument.Parse(await doneResponse.Content.ReadAsStringAsync());
        AssertItem(doneJson.RootElement.GetProperty("results")[0], b.Id, "UPDATED", "BLOCKED", "DONE");
        Assert.Equal(ActivityStatus.Done, (await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{b.Id}")).Status);
        var doneEvent = Assert.Single(_recorder.Events);
        Assert.Equal((b.Id, ActivityStatus.Blocked, ActivityStatus.Done), (doneEvent.ActivityId, doneEvent.PreviousStatus, doneEvent.NewStatus));
    }

    [Fact]
    public async Task BulkStatus_AC2_5_EveryItemFailing_StillReturns200_WithAllFailed_AndNothingChanges()
    {
        var otherStore = _api.As(StaffSeedData.OtherStoreManagerToken);
        var b = await CreateAsync("Store 2 restock", otherStore);
        _recorder.Clear();

        using var response = await _manager.PatchJsonAsync(BulkUrl, new { status = "DONE", activityIds = new[] { MissingId, b.Id } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        AssertCounts(root, requested: 2, updated: 0, unchanged: 0, failed: 2);
        AssertFailedItem(root.GetProperty("results")[0], MissingId, ErrorCodes.NotFound);
        AssertFailedItem(root.GetProperty("results")[1], b.Id, ErrorCodes.NotFound);
        var storedB = await otherStore.GetJsonAsync<ActivityDto>($"/api/activities/{b.Id}");
        Assert.Equal((ActivityStatus.Todo, b.UpdatedAt), (storedB.Status, storedB.UpdatedAt));
        Assert.Empty(_recorder.Events);
    }

    [Fact]
    public async Task BulkStatus_AC2_6_Associate_GetsForbiddenBeforeUnchanged_AndNotFoundBeforeForbidden()
    {
        var otherStore = _api.As(StaffSeedData.OtherStoreManagerToken);
        var a = await CreateAsync("Finished restock");
        (await _manager.PatchJsonAsync($"/api/activities/{a.Id}", new { status = "DONE" })).Dispose();
        var doneA = await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}");
        Assert.Equal(ActivityStatus.Done, doneA.Status);
        var b = await CreateAsync("Store 2 restock", otherStore);
        _recorder.Clear();

        using var response = await _api.As(StaffSeedData.AssociateToken)
            .PatchJsonAsync(BulkUrl, new { status = "DONE", activityIds = new[] { a.Id, b.Id } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        AssertCounts(root, requested: 2, updated: 0, unchanged: 0, failed: 2);
        AssertFailedItem(root.GetProperty("results")[0], a.Id, ErrorCodes.Forbidden);
        AssertFailedItem(root.GetProperty("results")[1], b.Id, ErrorCodes.NotFound);

        var storedA = await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}");
        Assert.Equal((ActivityStatus.Done, doneA.UpdatedAt), (storedA.Status, storedA.UpdatedAt));
        var storedB = await otherStore.GetJsonAsync<ActivityDto>($"/api/activities/{b.Id}");
        Assert.Equal((ActivityStatus.Todo, b.UpdatedAt), (storedB.Status, storedB.UpdatedAt));
        Assert.Empty(_recorder.Events);
    }

    [Fact]
    public async Task BulkStatus_AC3_1_EachUpdatedActivity_HasExactlyOneAuditEntry_OnTheWire()
    {
        var a = await CreateAsync("Restock aisle 1");
        var b = await CreateAsync("Restock aisle 2");
        Assert.Empty(await GetAuditAsync(_manager, a.Id));

        using var response = await _manager.PatchJsonAsync(BulkUrl, new { status = "DONE", activityIds = new[] { a.Id, b.Id } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal(2, json.RootElement.GetProperty("updated").GetInt32());
        }

        foreach (var id in new[] { a.Id, b.Id })
        {
            var entry = Assert.Single(await GetAuditAsync(_manager, id));
            AssertAuditEntry(entry, id, "TODO", "DONE", "usr-sm-001", note: null);
            var activity = await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{id}");
            Assert.Equal(activity.UpdatedAt, entry.GetProperty("occurredAt").GetDateTimeOffset());
        }

        Assert.Equal(2, _audit.All.Count);
        Assert.Equal([a.Id, b.Id], _audit.All.Select(e => e.ActivityId));
    }

    [Fact]
    public async Task BulkStatus_AC3_2_UnchangedAndFailedItems_HaveEmptyAuditTrails_OnlyTheUpdatedOneHasAnEntry()
    {
        var otherStore = _api.As(StaffSeedData.OtherStoreManagerToken);
        var a = await CreateAsync("Finished restock");
        var b = await CreateAsync("Stuck delivery");
        var c = await CreateAsync("Pending audit");
        var x = await CreateAsync("Store 2 restock", otherStore);
        (await _manager.PatchJsonAsync($"/api/activities/{a.Id}", new { status = "DONE" })).Dispose();
        (await _manager.PatchJsonAsync($"/api/activities/{b.Id}", new { status = "BLOCKED" })).Dispose();
        Assert.Empty(_audit.All); // the single PATCH writes no audit entries
        var doneA = await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}");
        _recorder.Clear();

        using var response = await _manager.PatchJsonAsync(BulkUrl, new { status = "BLOCKED", activityIds = new[] { a.Id, b.Id, c.Id, x.Id } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            var root = json.RootElement;
            AssertCounts(root, requested: 4, updated: 1, unchanged: 1, failed: 2);
            AssertFailedItem(root.GetProperty("results")[0], a.Id, ErrorCodes.BusinessRuleViolation);
            AssertItem(root.GetProperty("results")[1], b.Id, "UNCHANGED", "BLOCKED", "BLOCKED");
            AssertItem(root.GetProperty("results")[2], c.Id, "UPDATED", "TODO", "BLOCKED");
            AssertFailedItem(root.GetProperty("results")[3], x.Id, ErrorCodes.NotFound);
        }

        var storedA = await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}");
        Assert.Equal((ActivityStatus.Done, doneA.UpdatedAt), (storedA.Status, storedA.UpdatedAt));
        var storedX = await otherStore.GetJsonAsync<ActivityDto>($"/api/activities/{x.Id}");
        Assert.Equal((ActivityStatus.Todo, x.UpdatedAt), (storedX.Status, storedX.UpdatedAt));
        Assert.Equal(c.Id, Assert.Single(_recorder.Events).ActivityId);

        Assert.Empty(await GetAuditAsync(_manager, a.Id));
        Assert.Empty(await GetAuditAsync(_manager, b.Id));
        var entry = Assert.Single(await GetAuditAsync(_manager, c.Id));
        AssertAuditEntry(entry, c.Id, "TODO", "BLOCKED", "usr-sm-001", note: null);
        Assert.Empty(await GetAuditAsync(otherStore, x.Id));
        Assert.DoesNotContain(_audit.All, e => e.ActivityId == x.Id);
        Assert.Equal(c.Id, Assert.Single(_audit.All).ActivityId);
    }

    [Fact]
    public async Task BulkStatus_AC3_3_HandoverNote_IsOnEveryUpdatedEntry_AndA501CharacterNoteIs400OnNote_ChangingNothing()
    {
        const string Note = "Aisle 7 shelf damaged, waiting on maintenance";
        var a = await CreateAsync("Restock aisle 7");
        var b = await CreateAsync("Facing aisle 7");

        using (var first = await _api.As(StaffSeedData.DepartmentLeadToken)
            .PatchJsonAsync(BulkUrl, new { status = "BLOCKED", activityIds = new[] { a.Id, b.Id }, note = Note }))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        foreach (var id in new[] { a.Id, b.Id })
        {
            var entry = Assert.Single(await GetAuditAsync(_manager, id));
            AssertAuditEntry(entry, id, "TODO", "BLOCKED", "usr-dl-001", Note);
        }

        var blocked = await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}");
        _recorder.Clear();

        using var second = await _manager.PatchJsonAsync(BulkUrl, new { status = "DONE", activityIds = new[] { a.Id }, note = new string('x', 501) });

        await second.AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed, field: "note");
        var storedA = await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}");
        Assert.Equal((ActivityStatus.Blocked, blocked.UpdatedAt), (storedA.Status, storedA.UpdatedAt));
        Assert.Single(await GetAuditAsync(_manager, a.Id));
        Assert.Equal(2, _audit.All.Count);
        Assert.Empty(_recorder.Events);
    }

    [Fact]
    public async Task BulkStatus_AC3_3_NoteOfExactly500Characters_Returns200_AndIsStoredVerbatim()
    {
        var note = string.Concat(Enumerable.Repeat("Shelf 7 ", 62)) + "ok!!";
        Assert.Equal(500, note.Length);
        var a = await CreateAsync("Restock aisle 7");
        _recorder.Clear();

        using var response = await _manager.PatchJsonAsync(BulkUrl, new { status = "DONE", activityIds = new[] { a.Id }, note });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ActivityStatus.Done, (await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}")).Status);
        var entry = Assert.Single(await GetAuditAsync(_manager, a.Id));
        AssertAuditEntry(entry, a.Id, "TODO", "DONE", "usr-sm-001", note);
        Assert.Equal(a.Id, Assert.Single(_recorder.Events).ActivityId);
    }

    [Fact]
    public async Task BulkStatus_AC3_4_AuditOfOtherStoreOrMissingActivity_Is404_AndAnyUserOfTheStoreCanReadIt()
    {
        var a = await CreateAsync("Restock aisle 1");
        (await _manager.PatchJsonAsync(BulkUrl, new { status = "DONE", activityIds = new[] { a.Id } })).Dispose();
        Assert.Single(_audit.All);

        using var otherStore = await _api.As(StaffSeedData.OtherStoreManagerToken).GetAsync($"/api/activities/{a.Id}/audit");
        using var missing = await _manager.GetAsync($"/api/activities/{MissingId}/audit");

        var raw = await otherStore.Content.ReadAsStringAsync();
        await otherStore.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.DoesNotContain("aud-", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("previousStatus", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("BULK_STATUS_UPDATE", raw, StringComparison.Ordinal);
        await missing.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);

        var entry = Assert.Single(await GetAuditAsync(_api.As(StaffSeedData.AssociateToken), a.Id));
        AssertAuditEntry(entry, a.Id, "TODO", "DONE", "usr-sm-001", note: null);
    }

    [Fact]
    public async Task BulkStatus_AC3_4_AuditWithoutBearerToken_Returns401()
    {
        var a = await CreateAsync("Restock aisle 1");

        using var response = await _api.Anonymous.GetAsync($"/api/activities/{a.Id}/audit");

        await response.AssertErrorAsync(HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task BulkStatus_AC3_5_SuccessiveBulkUpdates_ReturnTwoEntries_OldestFirst()
    {
        var a = await CreateAsync("Planogram reset");

        (await _manager.PatchJsonAsync(BulkUrl, new { status = "BLOCKED", activityIds = new[] { a.Id } })).Dispose();
        (await _api.As(StaffSeedData.DepartmentLeadToken)
            .PatchJsonAsync(BulkUrl, new { status = "DONE", activityIds = new[] { a.Id }, note = "Fixed by night crew" })).Dispose();

        var entries = await GetAuditAsync(_manager, a.Id);
        Assert.Equal(2, entries.Count);
        AssertAuditEntry(entries[0], a.Id, "TODO", "BLOCKED", "usr-sm-001", note: null);
        AssertAuditEntry(entries[1], a.Id, "BLOCKED", "DONE", "usr-dl-001", "Fixed by night crew");
        Assert.True(entries[0].GetProperty("occurredAt").GetDateTimeOffset() <= entries[1].GetProperty("occurredAt").GetDateTimeOffset());
        Assert.NotEqual(entries[0].GetProperty("id").GetString(), entries[1].GetProperty("id").GetString());
        Assert.Equal(ActivityStatus.Done, (await _manager.GetJsonAsync<ActivityDto>($"/api/activities/{a.Id}")).Status);
    }

    private static void AssertAuditEntry(JsonElement entry, string activityId, string previousStatus, string newStatus, string changedBy, string? note)
    {
        Assert.StartsWith("aud-", entry.GetProperty("id").GetString(), StringComparison.Ordinal);
        Assert.Equal(activityId, entry.GetProperty("activityId").GetString());
        Assert.Equal(previousStatus, entry.GetProperty("previousStatus").GetString());
        Assert.Equal(newStatus, entry.GetProperty("newStatus").GetString());
        Assert.Equal(changedBy, entry.GetProperty("changedBy").GetString());
        Assert.Equal("BULK_STATUS_UPDATE", entry.GetProperty("source").GetString());
        if (note is null)
        {
            Assert.Equal(JsonValueKind.Null, entry.GetProperty("note").ValueKind);
        }
        else
        {
            Assert.Equal(note, entry.GetProperty("note").GetString());
        }

        Assert.Equal(JsonValueKind.String, entry.GetProperty("occurredAt").ValueKind);
    }

    private static async Task<List<JsonElement>> GetAuditAsync(HttpClient client, string activityId)
    {
        using var response = await client.GetAsync($"/api/activities/{activityId}/audit");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        return json.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static void AssertCounts(JsonElement root, int requested, int updated, int unchanged, int failed)
        => Assert.Equal(
            (requested, updated, unchanged, failed),
            (root.GetProperty("requested").GetInt32(), root.GetProperty("updated").GetInt32(), root.GetProperty("unchanged").GetInt32(), root.GetProperty("failed").GetInt32()));

    private static void AssertFailedItem(JsonElement item, string activityId, string code)
    {
        Assert.Equal(activityId, item.GetProperty("activityId").GetString());
        Assert.Equal("FAILED", item.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("previousStatus").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("newStatus").ValueKind);
        var error = item.GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }

    private static void AssertItem(JsonElement item, string activityId, string outcome, string previousStatus, string newStatus)
    {
        Assert.Equal(activityId, item.GetProperty("activityId").GetString());
        Assert.Equal(outcome, item.GetProperty("outcome").GetString());
        Assert.Equal(previousStatus, item.GetProperty("previousStatus").GetString());
        Assert.Equal(newStatus, item.GetProperty("newStatus").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("error").ValueKind);
    }

    private async Task<ActivityDto> CreateAsync(string title, HttpClient? creator = null, string? assigneeId = null)
    {
        using var response = await (creator ?? _manager).PostJsonAsync("/api/activities", new { title, assigneeId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<ActivityDto>();
    }

    /// <summary>Extra subscriber on the real in-memory bus, so HTTP tests see every published status change.</summary>
    private sealed class StatusChangedRecorder : IEventSubscriber<ActivityStatusChanged>
    {
        private readonly ConcurrentQueue<ActivityStatusChanged> _events = new();

        public IReadOnlyList<ActivityStatusChanged> Events => [.. _events];

        public void Clear() => _events.Clear();

        public Task HandleAsync(ActivityStatusChanged integrationEvent, CancellationToken cancellationToken)
        {
            _events.Enqueue(integrationEvent);
            return Task.CompletedTask;
        }
    }
}
