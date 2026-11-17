using Microsoft.AspNetCore.Http.HttpResults;
using StoreOps.Api.Modules.Programmes.Contracts;
using StoreOps.Api.Modules.Staff.Contracts;

namespace StoreOps.Api.Modules.Programmes;

/// <summary>HTTP layer only: binds input, calls <see cref="ProgrammeService"/>, shapes the response.</summary>
internal static class ProgrammeRoutes
{
    public static IEndpointRouteBuilder MapProgrammeRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/programmes").WithTags("Programmes");

        group.MapGet("/", ListProgrammesAsync).WithName("ListProgrammes");
        group.MapPost("/", CreateProgrammeAsync).WithName("CreateProgramme");
        group.MapPost("/{id}/members", AddProgrammeMemberAsync).WithName("AddProgrammeMember");

        return app;
    }

    private static async Task<Ok<IReadOnlyList<ProgrammeDto>>> ListProgrammesAsync(
        ProgrammeService service, CurrentUser user, CancellationToken cancellationToken)
        => TypedResults.Ok(await service.ListForStoreAsync(user, cancellationToken));

    private static async Task<Created<ProgrammeDto>> CreateProgrammeAsync(
        ProgrammeService service, CurrentUser user, CreateProgrammeRequest request, CancellationToken cancellationToken)
    {
        var programme = await service.CreateAsync(user, request, cancellationToken);
        return TypedResults.Created($"/api/programmes/{programme.Id}", programme);
    }

    private static async Task<Created<ProgrammeMemberDto>> AddProgrammeMemberAsync(
        ProgrammeService service, CurrentUser user, string id, AddProgrammeMemberRequest request, CancellationToken cancellationToken)
    {
        var member = await service.AddMemberAsync(user, id, request, cancellationToken);
        return TypedResults.Created($"/api/programmes/{id}/members/{member.UserId}", member);
    }
}
