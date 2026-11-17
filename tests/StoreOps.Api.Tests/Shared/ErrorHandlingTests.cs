using System.Net;
using Microsoft.Extensions.DependencyInjection;
using StoreOps.Api.Modules.Activities;
using StoreOps.Api.Modules.Activities.Contracts;
using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Tests.Infrastructure;

namespace StoreOps.Api.Tests.Shared;

/// <summary>Every failure path renders the same <c>{ error: { code, message, statusCode } }</c> envelope.</summary>
public sealed class ErrorHandlingTests
{
    public static TheoryData<AppError, string, int> AppErrors() => new()
    {
        { new ValidationError("bad", "field"), ErrorCodes.ValidationFailed, 400 },
        { new UnauthorizedError(), ErrorCodes.Unauthorized, 401 },
        { new ForbiddenError("no"), ErrorCodes.Forbidden, 403 },
        { new NotFoundError("Thing", "t-1"), ErrorCodes.NotFound, 404 },
        { new ConflictError("dup"), ErrorCodes.Conflict, 409 },
        { new BusinessRuleError("nope"), ErrorCodes.BusinessRuleViolation, 422 },
    };

    [Fact]
    public async Task UnexpectedException_Returns500Envelope_WithoutLeakingDetails()
    {
        using var api = new TestApi(services => services.AddSingleton<IActivityRepository, ExplodingActivityRepository>());

        using var response = await api.As(StaffSeedData.StoreManagerToken).GetAsync("/api/activities");

        var error = await response.AssertErrorAsync(HttpStatusCode.InternalServerError, ErrorCodes.InternalError);
        Assert.DoesNotContain("secret", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownRoute_Returns404Envelope()
    {
        using var api = new TestApi();

        using var response = await api.As(StaffSeedData.StoreManagerToken).GetAsync("/api/nothing-here");

        await response.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task UnsupportedMethod_Returns405Envelope()
    {
        using var api = new TestApi();

        using var response = await api.As(StaffSeedData.StoreManagerToken).DeleteAsync("/api/programmes");

        await response.AssertErrorAsync(HttpStatusCode.MethodNotAllowed, ErrorCodes.MethodNotAllowed);
    }

    [Fact]
    public async Task ErrorWithoutField_OmitsFieldFromTheEnvelope()
    {
        using var api = new TestApi();

        using var response = await api.As(StaffSeedData.StoreManagerToken).GetAsync("/api/activities/act-missing");

        Assert.DoesNotContain("\"field\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AppErrors))]
    public void AppErrorSubtypes_CarryCodeAndStatus(AppError error, string expectedCode, int expectedStatus)
    {
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(expectedStatus, error.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Theory]
    [InlineData(400, ErrorCodes.BadRequest)]
    [InlineData(403, ErrorCodes.Forbidden)]
    [InlineData(415, ErrorCodes.UnsupportedMediaType)]
    [InlineData(418, ErrorCodes.ClientError)]
    public void ForStatusCode_CodeAlwaysAgreesWithStatus(int status, string code)
    {
        Assert.Equal((code, status), (ErrorCodes.ForStatusCode(status).Code, ErrorCodes.ForStatusCode(status).StatusCode));
    }

    [Fact]
    public async Task NonJsonBody_Returns415Envelope()
    {
        using var api = new TestApi();
        using var content = new StringContent("title=x", System.Text.Encoding.UTF8, "text/plain");

        using var response = await api.As(StaffSeedData.AssociateToken).PostAsync(new Uri("/api/activities", UriKind.Relative), content);

        await response.AssertErrorAsync(HttpStatusCode.UnsupportedMediaType, ErrorCodes.UnsupportedMediaType);
    }

    private sealed class ExplodingActivityRepository : IActivityRepository
    {
        public Task<Activity?> GetByIdAsync(string id, CancellationToken cancellationToken) => throw Boom();

        public Task<IReadOnlyList<Activity>> ListByStoreAsync(
            string storeId, string? programmeId, ActivityStatus? status, CancellationToken cancellationToken) => throw Boom();

        public Task SaveAsync(Activity activity, CancellationToken cancellationToken) => throw Boom();

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken) => throw Boom();

        private static InvalidOperationException Boom() => new("secret connection string");
    }
}
