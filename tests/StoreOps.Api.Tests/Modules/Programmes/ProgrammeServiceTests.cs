using StoreOps.Api.Modules.Programmes;
using StoreOps.Api.Modules.Programmes.Contracts;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Tests.Infrastructure;

namespace StoreOps.Api.Tests.Modules.Programmes;

public sealed class ProgrammeServiceTests
{
    private readonly ModuleTestContext _ctx = new();

    [Fact]
    public async Task ListForStore_ReturnsOnlyTheCallersStoreProgrammes()
    {
        var store1 = await _ctx.Programmes.ListForStoreAsync(TestUsers.StoreManager, default);
        var store2 = await _ctx.Programmes.ListForStoreAsync(TestUsers.OtherStoreManager, default);

        Assert.Equal("prg-001", Assert.Single(store1).Id);
        Assert.Equal("prg-002", Assert.Single(store2).Id);
    }

    [Fact]
    public async Task Create_OwnsProgrammeByCallerAndStore_AndPublishesProgrammeCreated()
    {
        var created = await _ctx.Programmes.CreateAsync(
            TestUsers.StoreManager, new CreateProgrammeRequest("  Store refit  ", "Q1 refit", new DateOnly(2027, 1, 4), new DateOnly(2027, 2, 1)), default);

        Assert.Equal("Store refit", created.Name);
        Assert.Equal("store-001", created.StoreId);
        Assert.Equal(TestUsers.StoreManager.UserId, created.CreatedBy);
        Assert.Empty(created.Members);
        var evt = Assert.Single(_ctx.Events.Of<ProgrammeCreated>());
        Assert.Equal(created.Id, evt.ProgrammeId);
        Assert.Contains(await _ctx.Programmes.ListForStoreAsync(TestUsers.StoreManager, default), p => p.Id == created.Id);
    }

    [Fact]
    public async Task Create_WithoutName_ThrowsValidationError()
    {
        var error = await Assert.ThrowsAsync<ValidationError>(
            () => _ctx.Programmes.CreateAsync(TestUsers.StoreManager, new CreateProgrammeRequest(" ", null, null, null), default));

        Assert.Equal("name", error.Field);
    }

    [Fact]
    public async Task Create_EndingBeforeItStarts_ThrowsValidationError()
    {
        var error = await Assert.ThrowsAsync<ValidationError>(() => _ctx.Programmes.CreateAsync(
            TestUsers.StoreManager, new CreateProgrammeRequest("Backwards", null, new DateOnly(2027, 2, 1), new DateOnly(2027, 1, 1)), default));

        Assert.Equal("endsOn", error.Field);
    }

    [Fact]
    public async Task Create_DuplicateNameInSameStore_ThrowsConflict_ButOtherStoreMayReuseIt()
    {
        await Assert.ThrowsAsync<ConflictError>(() => _ctx.Programmes.CreateAsync(
            TestUsers.StoreManager, new CreateProgrammeRequest("holiday readiness", null, null, null), default));

        var otherStore = await _ctx.Programmes.CreateAsync(
            TestUsers.OtherStoreManager, new CreateProgrammeRequest("Holiday Readiness", null, null, null), default);
        Assert.Equal("store-002", otherStore.StoreId);
    }

    [Fact]
    public async Task AddMember_AddsMemberWithRole_AndPublishesProgrammeMemberAdded()
    {
        var member = await _ctx.Programmes.AddMemberAsync(
            TestUsers.StoreManager, "prg-001", new AddProgrammeMemberRequest("usr-dl-001", ProgrammeRole.DepartmentLead), default);

        Assert.Equal(ProgrammeRole.DepartmentLead, member.Role);
        var programme = await _ctx.Programmes.FindByIdAsync("prg-001", default);
        Assert.Equal(member, Assert.Single(programme!.Members));
        var evt = Assert.Single(_ctx.Events.Of<ProgrammeMemberAdded>());
        Assert.Equal(("prg-001", "usr-dl-001", TestUsers.StoreManager.UserId), (evt.ProgrammeId, evt.UserId, evt.AddedBy));
    }

    [Theory]
    [InlineData(null, ProgrammeRole.Associate, "userId")]
    [InlineData("usr-as-001", null, "role")]
    public async Task AddMember_WithMissingField_ThrowsValidationError(string? userId, ProgrammeRole? role, string field)
    {
        var error = await Assert.ThrowsAsync<ValidationError>(() => _ctx.Programmes.AddMemberAsync(
            TestUsers.StoreManager, "prg-001", new AddProgrammeMemberRequest(userId, role), default));

        Assert.Equal(field, error.Field);
    }

    [Theory]
    [InlineData("prg-missing", "usr-as-001", typeof(NotFoundError))]
    [InlineData("prg-002", "usr-as-001", typeof(NotFoundError))]
    [InlineData("prg-001", "usr-missing", typeof(NotFoundError))]
    [InlineData("prg-001", "usr-sm-002", typeof(BusinessRuleError))]
    [InlineData("prg-001", "usr-as-002", typeof(BusinessRuleError))]
    public async Task AddMember_RejectsInvisibleProgrammesAndIneligibleStaff(string programmeId, string userId, Type expected)
    {
        // Unknown or other-store programme → 404; unknown user → 404; other-store or inactive user → 422.
        var error = await Assert.ThrowsAnyAsync<AppError>(() => _ctx.Programmes.AddMemberAsync(
            TestUsers.StoreManager, programmeId, new AddProgrammeMemberRequest(userId, ProgrammeRole.Associate), default));

        Assert.IsType(expected, error);
        Assert.Empty(_ctx.Events.Published);
    }

    [Fact]
    public async Task AddMember_Twice_ThrowsConflict()
    {
        var request = new AddProgrammeMemberRequest("usr-as-001", ProgrammeRole.Associate);
        await _ctx.Programmes.AddMemberAsync(TestUsers.StoreManager, "prg-001", request, default);

        await Assert.ThrowsAsync<ConflictError>(() => _ctx.Programmes.AddMemberAsync(TestUsers.StoreManager, "prg-001", request, default));
    }
}
