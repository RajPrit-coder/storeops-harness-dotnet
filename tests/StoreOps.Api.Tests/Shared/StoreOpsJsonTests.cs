using StoreOps.Api.Modules.Activities.Contracts;
using StoreOps.Api.Modules.Alerts.Contracts;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Shared.Http;

namespace StoreOps.Api.Tests.Shared;

public sealed class StoreOpsJsonTests
{
    [Fact]
    public void WireName_UsesTheSpecsScreamingSnakeValues()
    {
        Assert.Equal("IN_PROGRESS", StoreOpsJson.WireName(ActivityStatus.InProgress));
        Assert.Equal("SLA_BREACH", StoreOpsJson.WireName(AlertType.SlaBreach));
        Assert.Equal("IN_APP", StoreOpsJson.WireName(NotificationChannel.InApp));
    }

    [Fact]
    public void ParseQuery_AcceptsExactWireValues()
    {
        Assert.Equal(ActivityStatus.InProgress, StoreOpsJson.ParseQuery<ActivityStatus>("IN_PROGRESS", "status"));
    }

    [Theory]
    [InlineData("blocked")]
    [InlineData(" BLOCKED ")]
    [InlineData("Blocked")]
    public void ParseQuery_RejectsOtherCasingsAndSpacing(string raw)
    {
        Assert.Throws<ValidationError>(() => StoreOpsJson.ParseQuery<ActivityStatus>(raw, "status"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ParseQuery_WithNoValue_ReturnsNull(string? raw)
    {
        Assert.Null(StoreOpsJson.ParseQuery<ActivityStatus>(raw, "status"));
    }

    [Fact]
    public void ParseQuery_WithUnknownValue_ThrowsValidationErrorListingAllowedValues()
    {
        var error = Assert.Throws<ValidationError>(() => StoreOpsJson.ParseQuery<ActivityStatus>("InProgress", "status"));

        Assert.Equal("status", error.Field);
        Assert.Contains("TODO, IN_PROGRESS, DONE, BLOCKED", error.Message, StringComparison.Ordinal);
    }
}
