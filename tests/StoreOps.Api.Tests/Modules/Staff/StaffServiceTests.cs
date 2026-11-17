using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Modules.Staff.Contracts;
using StoreOps.Api.Tests.Infrastructure;

namespace StoreOps.Api.Tests.Modules.Staff;

public sealed class StaffServiceTests
{
    private readonly ModuleTestContext _ctx = new();

    [Fact]
    public async Task Authenticate_WithValidToken_ReturnsTheUsersIdentityStoreAndRole()
    {
        var user = await _ctx.Staff.AuthenticateAsync(StaffSeedData.DepartmentLeadToken, default);

        Assert.Equal(TestUsers.DepartmentLead, user);
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData(StaffSeedData.ExpiredToken)]
    [InlineData(StaffSeedData.InactiveUserToken)]
    public async Task Authenticate_WithUnknownExpiredOrInactiveUsersToken_ReturnsNull(string token)
    {
        Assert.Null(await _ctx.Staff.AuthenticateAsync(token, default));
    }

    [Fact]
    public async Task Directory_FindsStaffAndListsAStoresStaff()
    {
        var lead = await _ctx.Staff.FindByIdAsync("usr-dl-001", default);
        var store2 = await _ctx.Staff.ListByStoreAsync("store-002", default);

        Assert.Equal(("GROCERY", StaffRole.DepartmentLead), (lead?.Department, lead?.Role));
        Assert.Null(await _ctx.Staff.FindByIdAsync("usr-missing", default));
        Assert.Equal("usr-sm-002", Assert.Single(store2).Id);
    }
}
