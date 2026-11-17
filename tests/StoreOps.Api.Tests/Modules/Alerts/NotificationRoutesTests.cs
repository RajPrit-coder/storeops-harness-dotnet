using StoreOps.Api.Modules.Alerts.Contracts;
using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Tests.Infrastructure;
using StoreOps.Api.Tests.Modules.Activities;

namespace StoreOps.Api.Tests.Modules.Alerts;

/// <summary><c>GET /api/alerts</c> (spec §3.6) and the event-bus path that feeds it.</summary>
public sealed class NotificationRoutesTests
{
    [Fact]
    public async Task List_WhenNothingHappened_ReturnsEmptyArray()
    {
        using var api = new TestApi();

        var alerts = await api.As(StaffSeedData.StoreManagerToken).GetJsonAsync<List<NotificationDto>>("/api/alerts");

        Assert.Empty(alerts);
    }

    [Fact]
    public async Task BlockingAnActivity_RaisesEscalationForTheStoreManager_ViaTheEventBus()
    {
        using var api = new TestApi();
        var associate = api.As(StaffSeedData.AssociateToken);
        var activity = await ActivityRoutesTests.CreateAsync(associate, "Fix freezer door");

        (await associate.PatchJsonAsync($"/api/activities/{activity.Id}", new { status = "BLOCKED" })).Dispose();

        var managerAlerts = await api.As(StaffSeedData.StoreManagerToken).GetJsonAsync<List<NotificationDto>>("/api/alerts");
        var alert = Assert.Single(managerAlerts);
        Assert.Equal(AlertType.Escalation, alert.Type);
        Assert.Equal(activity.Id, alert.SourceActivityId);

        // GET /api/alerts is per authenticated user: the associate who blocked it gets nothing.
        Assert.Empty(await associate.GetJsonAsync<List<NotificationDto>>("/api/alerts"));
    }
}
