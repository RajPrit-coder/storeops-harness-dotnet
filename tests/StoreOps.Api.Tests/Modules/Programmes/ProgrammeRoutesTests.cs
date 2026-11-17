using System.Net;
using System.Text.Json;
using StoreOps.Api.Modules.Programmes.Contracts;
using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Tests.Infrastructure;

namespace StoreOps.Api.Tests.Modules.Programmes;

/// <summary>The three programmes endpoints of spec §3.6, over HTTP.</summary>
public sealed class ProgrammeRoutesTests
{
    [Fact]
    public async Task List_ReturnsProgrammesOfTheAuthenticatedStoreOnly()
    {
        using var api = new TestApi();

        var programmes = await api.As(StaffSeedData.AssociateToken).GetJsonAsync<List<ProgrammeDto>>("/api/programmes");

        Assert.Equal("prg-001", Assert.Single(programmes).Id);
    }

    [Fact]
    public async Task Post_Returns201_AndProgrammeIsListedForTheStore()
    {
        using var api = new TestApi();
        var client = api.As(StaffSeedData.StoreManagerToken);

        using var response = await client.PostJsonAsync("/api/programmes", new { name = "Store refit", startsOn = "2027-01-04" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadJsonAsync<ProgrammeDto>();
        Assert.Equal($"/api/programmes/{created.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(new DateOnly(2027, 1, 4), created.StartsOn);
        Assert.Contains(await client.GetJsonAsync<List<ProgrammeDto>>("/api/programmes"), p => p.Id == created.Id);
    }

    [Fact]
    public async Task Post_DuplicateName_Returns409Envelope()
    {
        using var api = new TestApi();

        using var response = await api.As(StaffSeedData.StoreManagerToken).PostJsonAsync("/api/programmes", new { name = "Holiday Readiness" });

        await response.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.Conflict);
    }

    [Fact]
    public async Task AddMember_Returns201WithSpecRole_AndMemberIsOnTheProgramme()
    {
        using var api = new TestApi();
        var client = api.As(StaffSeedData.StoreManagerToken);

        using var response = await client.PostJsonAsync("/api/programmes/prg-001/members", new { userId = "usr-dl-001", role = "DEPARTMENT_LEAD" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/programmes/prg-001/members/usr-dl-001", response.Headers.Location?.OriginalString);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DEPARTMENT_LEAD", json.RootElement.GetProperty("role").GetString());

        var programme = Assert.Single(await client.GetJsonAsync<List<ProgrammeDto>>("/api/programmes"));
        Assert.Equal(("usr-dl-001", ProgrammeRole.DepartmentLead), (Assert.Single(programme.Members).UserId, programme.Members[0].Role));
    }

    [Fact]
    public async Task AddMember_ToAnotherStoresProgramme_Returns404()
    {
        using var api = new TestApi();

        using var response = await api.As(StaffSeedData.StoreManagerToken)
            .PostJsonAsync("/api/programmes/prg-002/members", new { userId = "usr-as-001", role = "ASSOCIATE" });

        await response.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task AddMember_InactiveStaff_Returns422()
    {
        using var api = new TestApi();

        using var response = await api.As(StaffSeedData.StoreManagerToken)
            .PostJsonAsync("/api/programmes/prg-001/members", new { userId = "usr-as-002", role = "ASSOCIATE" });

        await response.AssertErrorAsync(HttpStatusCode.UnprocessableEntity, ErrorCodes.BusinessRuleViolation);
    }

    [Theory]
    [InlineData("INTERN")]
    [InlineData("DepartmentLead")]
    [InlineData("department_lead")]
    public async Task AddMember_WithRoleNotInSpecWireFormat_Returns400_AndAddsNobody(string role)
    {
        using var api = new TestApi();
        var client = api.As(StaffSeedData.StoreManagerToken);

        using var response = await client.PostJsonAsync("/api/programmes/prg-001/members", new { userId = "usr-as-001", role });

        await response.AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed, field: "programmeRole");
        Assert.Empty(Assert.Single(await client.GetJsonAsync<List<ProgrammeDto>>("/api/programmes")).Members);
    }
}
