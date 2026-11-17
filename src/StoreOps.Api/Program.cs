using Microsoft.AspNetCore.Authorization;
using StoreOps.Api.Modules.Activities;
using StoreOps.Api.Modules.Alerts;
using StoreOps.Api.Modules.Programmes;
using StoreOps.Api.Modules.Reports;
using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Shared.Events;
using StoreOps.Api.Shared.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options => StoreOpsJson.Apply(options.SerializerOptions));

// Surface binding failures (malformed JSON, bad enum values) as exceptions in every environment,
// so AppErrorHandler renders them in the standard error envelope.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppErrorHandler>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddInMemoryEventBus();

// Every endpoint requires an authenticated caller unless it opts out explicitly.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services
    .AddStaffModule()
    .AddProgrammesModule()
    .AddActivitiesModule()
    .AddAlertsModule()
    .AddReportsModule();

var app = builder.Build();

app.UseExceptionHandler();
app.UseErrorEnvelopeForBareStatusCodes();
app.UseAuthentication();
app.UseAuthorization();

app.MapActivitiesModule();
app.MapProgrammesModule();
app.MapStaffModule();
app.MapAlertsModule();
app.MapReportsModule();

await app.RunAsync();

/// <summary>Entry point, exposed so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
