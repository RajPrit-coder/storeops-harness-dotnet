using StoreOps.Api.Modules.Activities.Contracts;
using StoreOps.Api.Modules.Alerts.Contracts;
using StoreOps.Api.Tests.Infrastructure;

namespace StoreOps.Api.Tests.Modules.Alerts;

public sealed class NotificationServiceTests
{
    private readonly ModuleTestContext _ctx = new();

    [Fact]
    public async Task EscalateBlockedActivity_NotifiesEachActiveStoreManagerOfTheStore_InApp()
    {
        var created = await _ctx.Notifications.EscalateBlockedActivityAsync(StatusChanged(ActivityStatus.Blocked), default);

        // store-001 has exactly one active STORE_MANAGER (usr-sm-001); the regional manager is not notified.
        var alert = Assert.Single(created);
        Assert.Equal("usr-sm-001", alert.RecipientId);
        Assert.Equal(AlertType.Escalation, alert.Type);
        Assert.Equal(NotificationChannel.InApp, alert.Channel);
        Assert.Equal(NotificationStatus.Unread, alert.Status);
        Assert.Equal("act-1", alert.SourceActivityId);
        Assert.Equal(alert.Id, Assert.Single(_ctx.Events.Of<NotificationCreated>()).NotificationId);
    }

    [Theory]
    [InlineData(ActivityStatus.InProgress)]
    [InlineData(ActivityStatus.Done)]
    public async Task EscalateBlockedActivity_ForOtherStatuses_CreatesNothing(ActivityStatus newStatus)
    {
        var created = await _ctx.Notifications.EscalateBlockedActivityAsync(StatusChanged(newStatus), default);

        Assert.Empty(created);
        Assert.Empty(_ctx.Events.Published);
    }

    [Fact]
    public async Task ListForUser_ReturnsOnlyAlertsAddressedToThatUser()
    {
        await _ctx.Notifications.EscalateBlockedActivityAsync(StatusChanged(ActivityStatus.Blocked), default);

        Assert.Single(await _ctx.Notifications.ListForUserAsync(TestUsers.StoreManager, default));
        Assert.Empty(await _ctx.Notifications.ListForUserAsync(TestUsers.Associate, default));
    }

    private ActivityStatusChanged StatusChanged(ActivityStatus newStatus)
        => new("act-1", "store-001", "Restock dairy", ActivityStatus.Todo, newStatus, ActivityPriority.High, "usr-as-001", "usr-as-001", _ctx.Time.Now);
}
