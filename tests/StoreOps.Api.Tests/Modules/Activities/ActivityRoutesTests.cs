using System.Net;
using System.Text;
using System.Text.Json;
using StoreOps.Api.Modules.Activities.Contracts;
using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Tests.Infrastructure;

namespace StoreOps.Api.Tests.Modules.Activities;

/// <summary>The five activities endpoints of spec §3.6, over HTTP.</summary>
public sealed class ActivityRoutesTests
{
    [Fact]
    public async Task Endpoints_WithoutToken_Return401Envelope()
    {
        using var api = new TestApi();

        using var response = await api.Anonymous.GetAsync("/api/activities");

        await response.AssertErrorAsync(HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task Post_Returns201WithLocation_AndUsesSpecWireValues()
    {
        using var api = new TestApi();
        var client = api.As(StaffSeedData.AssociateToken);

        using var response = await client.PostJsonAsync("/api/activities", new
        {
            title = "Planogram reset aisle 4",
            programmeId = "prg-001",
            priority = "CRITICAL",
            category = "PLANOGRAM",
            department = "GROCERY",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetString();
        Assert.Equal($"/api/activities/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal("TODO", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("CRITICAL", json.RootElement.GetProperty("priority").GetString());
        Assert.Equal("PLANOGRAM", json.RootElement.GetProperty("category").GetString());
        Assert.Equal("usr-as-001", json.RootElement.GetProperty("ownerId").GetString());
    }

    [Fact]
    public async Task Post_WithoutTitle_Returns400ValidationEnvelope()
    {
        using var api = new TestApi();

        using var response = await api.As(StaffSeedData.AssociateToken).PostJsonAsync("/api/activities", new { category = "AUDIT" });

        await response.AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed, field: "title");
    }

    [Theory]
    [InlineData("URGENT")]
    [InlineData("High")]
    [InlineData("high")]
    public async Task Post_WithPriorityNotInSpecWireFormat_Returns400ValidationEnvelope(string priority)
    {
        using var api = new TestApi();

        using var response = await api.As(StaffSeedData.AssociateToken).PostJsonAsync("/api/activities", new { title = "x", priority });

        await response.AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed, field: "activityPriority");
    }

    [Fact]
    public async Task Post_WithMalformedJson_Returns400BadRequestEnvelope()
    {
        using var api = new TestApi();
        using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        using var response = await api.As(StaffSeedData.AssociateToken).PostAsync(new Uri("/api/activities", UriKind.Relative), content);

        await response.AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.BadRequest);
    }

    [Fact]
    public async Task GetById_ReturnsActivity_AndUnknownIdReturns404Envelope()
    {
        using var api = new TestApi();
        var client = api.As(StaffSeedData.AssociateToken);
        var created = await CreateAsync(client, "Audit");

        var fetched = await client.GetJsonAsync<ActivityDto>($"/api/activities/{created.Id}");
        using var missing = await client.GetAsync("/api/activities/act-missing");

        Assert.Equal(created, fetched);
        await missing.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task GetById_FromAnotherStore_Returns404()
    {
        using var api = new TestApi();
        var created = await CreateAsync(api.As(StaffSeedData.AssociateToken), "Store 1 only");

        using var response = await api.As(StaffSeedData.OtherStoreManagerToken).GetAsync($"/api/activities/{created.Id}");

        await response.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task List_FiltersByProgrammeAndStatusWireValue()
    {
        using var api = new TestApi();
        var client = api.As(StaffSeedData.StoreManagerToken);
        var inProgramme = await CreateAsync(client, "Holiday window", programmeId: "prg-001");
        var other = await CreateAsync(client, "Loose task");
        (await client.PatchJsonAsync($"/api/activities/{other.Id}", new { status = "IN_PROGRESS" })).Dispose();

        var byProgramme = await client.GetJsonAsync<List<ActivityDto>>("/api/activities?programmeId=prg-001");
        var inProgress = await client.GetJsonAsync<List<ActivityDto>>("/api/activities?status=IN_PROGRESS");

        Assert.Equal(inProgramme.Id, Assert.Single(byProgramme).Id);
        Assert.Equal(other.Id, Assert.Single(inProgress).Id);
    }

    [Fact]
    public async Task List_WithUnknownStatus_Returns400ValidationEnvelope()
    {
        using var api = new TestApi();

        using var response = await api.As(StaffSeedData.StoreManagerToken).GetAsync("/api/activities?status=FINISHED");

        await response.AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed, field: "status");
    }

    [Fact]
    public async Task Patch_UpdatesFields_AndChangeIsVisibleOnGet()
    {
        using var api = new TestApi();
        var client = api.As(StaffSeedData.StoreManagerToken);
        var created = await CreateAsync(client, "Shelf audit");

        using var response = await client.PatchJsonAsync($"/api/activities/{created.Id}", new
        {
            status = "BLOCKED",
            priority = "HIGH",
            category = "AUDIT",
            assigneeId = "usr-dl-001",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await client.GetJsonAsync<ActivityDto>($"/api/activities/{created.Id}");
        Assert.Equal(ActivityStatus.Blocked, fetched.Status);
        Assert.Equal(ActivityPriority.High, fetched.Priority);
        Assert.Equal(ActivityCategory.Audit, fetched.Category);
        Assert.Equal("usr-dl-001", fetched.AssigneeId);
    }

    [Fact]
    public async Task Patch_ReopeningDoneActivity_Returns422()
    {
        using var api = new TestApi();
        var client = api.As(StaffSeedData.StoreManagerToken);
        var created = await CreateAsync(client, "Count stock");
        (await client.PatchJsonAsync($"/api/activities/{created.Id}", new { status = "DONE" })).Dispose();

        using var response = await client.PatchJsonAsync($"/api/activities/{created.Id}", new { status = "TODO" });

        await response.AssertErrorAsync(HttpStatusCode.UnprocessableEntity, ErrorCodes.BusinessRuleViolation);
    }

    [Fact]
    public async Task Delete_ByNonOwnerAssociateOrLead_Returns403_AndActivityStillExists()
    {
        using var api = new TestApi();
        var owner = api.As(StaffSeedData.AssociateToken);
        var created = await CreateAsync(owner, "Owned by the associate");

        using var response = await api.As(StaffSeedData.DepartmentLeadToken).DeleteAsync($"/api/activities/{created.Id}");

        await response.AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        Assert.Equal(created.Id, (await owner.GetJsonAsync<ActivityDto>($"/api/activities/{created.Id}")).Id);
    }

    [Theory]
    [InlineData(StaffSeedData.AssociateToken)]
    [InlineData(StaffSeedData.StoreManagerToken)]
    public async Task Delete_ByOwnerOrStoreManager_Returns204_AndActivityIsGone(string deleterToken)
    {
        using var api = new TestApi();
        var created = await CreateAsync(api.As(StaffSeedData.AssociateToken), "Disposable");
        var deleter = api.As(deleterToken);

        using var response = await deleter.DeleteAsync($"/api/activities/{created.Id}");
        using var after = await deleter.GetAsync($"/api/activities/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await after.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    internal static async Task<ActivityDto> CreateAsync(HttpClient client, string title, string? programmeId = null)
    {
        using var response = await client.PostJsonAsync("/api/activities", new { title, programmeId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<ActivityDto>();
    }
}
