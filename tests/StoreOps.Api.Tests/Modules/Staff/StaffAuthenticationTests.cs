using System.Net;
using System.Net.Http.Headers;
using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Tests.Infrastructure;

namespace StoreOps.Api.Tests.Modules.Staff;

/// <summary>Staff is auth-only (spec §3.6): it has no endpoints but authenticates every request.</summary>
public sealed class StaffAuthenticationTests
{
    [Theory]
    [InlineData("Bearer", "not-a-token")]
    [InlineData("Bearer", StaffSeedData.ExpiredToken)]
    [InlineData("Bearer", StaffSeedData.InactiveUserToken)]
    [InlineData("Basic", StaffSeedData.StoreManagerToken)]
    public async Task Request_WithBadCredentials_Returns401Envelope(string scheme, string token)
    {
        using var api = new TestApi();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/programmes", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue(scheme, token);

        using var response = await api.Anonymous.SendAsync(request);

        await response.AssertErrorAsync(HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task DevAutoLogin_SignsInRequestsWithoutAHeader_ButStillRejectsBadTokens()
    {
        using var api = new TestApi(configureSettings: s => s[StaffAuthenticationHandler.DevAutoLoginTokenKey] = StaffSeedData.StoreManagerToken);

        var programmes = await api.Anonymous.GetJsonAsync<List<StoreOps.Api.Modules.Programmes.Contracts.ProgrammeDto>>("/api/programmes");
        using var badToken = await api.As("not-a-token").GetAsync("/api/programmes");

        Assert.Equal("prg-001", Assert.Single(programmes).Id);
        await badToken.AssertErrorAsync(HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task StaffModule_ExposesNoEndpoints()
    {
        using var api = new TestApi();

        using var response = await api.As(StaffSeedData.StoreManagerToken).GetAsync("/api/staff");

        await response.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }
}
