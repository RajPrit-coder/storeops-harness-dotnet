using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using StoreOps.Api.Modules.Staff.Contracts;

namespace StoreOps.Api.Modules.Staff;

/// <summary>
/// Inbound adapter for <c>Authorization: Bearer &lt;token&gt;</c>. Like routes, it holds no logic of its
/// own and delegates to <see cref="StaffService"/>.
/// When <c>Staff:DevAutoLoginToken</c> is configured (only appsettings.Development.json sets it), a request
/// WITHOUT an Authorization header is signed in with that token, so the API can be tried from a browser.
/// A request that sends a bad token still gets 401.
/// </summary>
internal sealed class StaffAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration,
    StaffService staffService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "StoreOpsBearer";
    public const string DevAutoLoginTokenKey = "Staff:DevAutoLoginToken";

    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (string.IsNullOrEmpty(header))
        {
            var devToken = configuration[DevAutoLoginTokenKey];
            if (string.IsNullOrEmpty(devToken))
            {
                return AuthenticateResult.NoResult();
            }

            header = BearerPrefix + devToken;
        }

        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.Fail("Authorization header must use the Bearer scheme.");
        }

        var user = await staffService.AuthenticateAsync(header[BearerPrefix.Length..].Trim(), Context.RequestAborted);
        if (user is null)
        {
            return AuthenticateResult.Fail("Invalid or expired token.");
        }

        Claim[] claims =
        [
            new(StaffClaimTypes.UserId, user.UserId),
            new(StaffClaimTypes.StoreId, user.StoreId),
            new(StaffClaimTypes.RegionId, user.RegionId),
            new(StaffClaimTypes.Role, user.Role.ToString()),
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
