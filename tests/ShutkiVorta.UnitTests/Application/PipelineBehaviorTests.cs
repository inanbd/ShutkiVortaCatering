using FluentValidation;
using MediatR;
using ShutkiVorta.Application.Common.Behaviors;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.UnitTests.TestDoubles;
using ValidationException = ShutkiVorta.Application.Common.Exceptions.ValidationException;

namespace ShutkiVorta.UnitTests.Application;

public sealed class PipelineBehaviorTests
{
    private sealed record AdminRequest : IRequest<string>, IRequireAdmin;

    private sealed record StaffRequest : IRequest<string>, IRequireStaff;

    [RequirePermission(Permissions.MenuManage)]
    private sealed record MenuManageRequest : IRequest<string>;

    private sealed record CustomerRequest : IRequest<string>, IRequireAuthenticatedUser;

    private sealed record PublicRequest : IRequest<string>;

    private sealed record NamedRequest(string Name) : IRequest<string>;

    private sealed class NamedRequestValidator : AbstractValidator<NamedRequest>
    {
        public NamedRequestValidator() => RuleFor(x => x.Name).NotEmpty();
    }

    private static readonly FakeCurrentUser SignedIn = new() { UserId = "u1" };

    private static Task<string> Ok(CancellationToken _) => Task.FromResult("ok");

    private static AuthorizationBehavior<TRequest, string> Behavior<TRequest>(ICurrentStaff staff, FakeCurrentUser? user = null)
        where TRequest : notnull =>
        new(user ?? SignedIn, staff);

    [Fact]
    public async Task AdminRequests_BlockManagersAndCustomers()
    {
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Behavior<AdminRequest>(FakeCurrentStaff.Defaults(StaffRole.Manager)).Handle(new AdminRequest(), Ok, CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Behavior<AdminRequest>(FakeCurrentStaff.None()).Handle(new AdminRequest(), Ok, CancellationToken.None));
    }

    [Fact]
    public async Task AdminRequests_AllowAdmins()
    {
        Assert.Equal("ok", await Behavior<AdminRequest>(FakeCurrentStaff.Admin()).Handle(new AdminRequest(), Ok, CancellationToken.None));
    }

    [Fact]
    public async Task StaffRequests_AllowAnyStaffMember_ButNotCustomers()
    {
        Assert.Equal("ok", await Behavior<StaffRequest>(FakeCurrentStaff.With(StaffRole.Employee)).Handle(new StaffRequest(), Ok, CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Behavior<StaffRequest>(FakeCurrentStaff.None()).Handle(new StaffRequest(), Ok, CancellationToken.None));
    }

    [Fact]
    public async Task PermissionRequests_NeedThatPermission()
    {
        Assert.Equal("ok", await Behavior<MenuManageRequest>(FakeCurrentStaff.With(StaffRole.Employee, Permissions.MenuManage))
            .Handle(new MenuManageRequest(), Ok, CancellationToken.None));
        Assert.Equal("ok", await Behavior<MenuManageRequest>(FakeCurrentStaff.Admin()).Handle(new MenuManageRequest(), Ok, CancellationToken.None));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Behavior<MenuManageRequest>(FakeCurrentStaff.With(StaffRole.Manager, Permissions.MenuView)).Handle(new MenuManageRequest(), Ok, CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Behavior<MenuManageRequest>(FakeCurrentStaff.None()).Handle(new MenuManageRequest(), Ok, CancellationToken.None));
    }

    [Fact]
    public async Task PermissionRequests_RefuseUnconfirmedStaff()
    {
        var unconfirmed = new FakeCurrentStaff(StaffAccess.Evaluate(
            "u1", [StaffRole.Admin], emailConfirmed: false, usesCustom: false, [], new Dictionary<StaffRole, IReadOnlyDictionary<string, bool>>()));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Behavior<AdminRequest>(unconfirmed).Handle(new AdminRequest(), Ok, CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Behavior<StaffRequest>(unconfirmed).Handle(new StaffRequest(), Ok, CancellationToken.None));
    }

    [Fact]
    public async Task Authorization_RequiresSignInForCustomerRequests()
    {
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Behavior<CustomerRequest>(FakeCurrentStaff.None(), new FakeCurrentUser()).Handle(new CustomerRequest(), Ok, CancellationToken.None));
        Assert.Equal("ok", await Behavior<CustomerRequest>(FakeCurrentStaff.None()).Handle(new CustomerRequest(), Ok, CancellationToken.None));
    }

    [Fact]
    public async Task PublicRequests_NeverReadStaffAccess()
    {
        var staff = new CountingStaff();
        Assert.Equal("ok", await Behavior<PublicRequest>(staff, new FakeCurrentUser()).Handle(new PublicRequest(), Ok, CancellationToken.None));
        Assert.Equal("ok", await Behavior<CustomerRequest>(staff).Handle(new CustomerRequest(), Ok, CancellationToken.None));
        Assert.Equal(0, staff.Reads);
    }

    [Fact]
    public async Task CurrentStaff_ReadsTheDatabaseOncePerRequest_AndNeverForAnonymousVisitors()
    {
        var service = new FakeStaffAccessService();
        service.Users["u1"] = StaffAccessFor.Admin("u1");

        var current = new CurrentStaff(SignedIn, service);
        Assert.True((await current.GetAsync()).IsAdmin);
        Assert.True((await current.GetAsync()).IsAdmin);
        Assert.Equal(1, service.Reads);

        var anonymous = new CurrentStaff(new FakeCurrentUser(), service);
        Assert.Same(StaffAccess.None, await anonymous.GetAsync());
        Assert.Equal(1, service.Reads);
    }

    [Fact]
    public async Task Validation_ThrowsWithPropertyErrors()
    {
        var behavior = new ValidationBehavior<NamedRequest, string>([new NamedRequestValidator()]);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => behavior.Handle(new NamedRequest(""), Ok, CancellationToken.None));
        Assert.True(ex.Errors.ContainsKey(nameof(NamedRequest.Name)));
    }

    [Fact]
    public async Task Validation_PassesValidRequests()
    {
        var behavior = new ValidationBehavior<NamedRequest, string>([new NamedRequestValidator()]);
        Assert.Equal("ok", await behavior.Handle(new NamedRequest("Aloo"), Ok, CancellationToken.None));
    }

    private sealed class CountingStaff : ICurrentStaff
    {
        public int Reads { get; private set; }

        public ValueTask<StaffAccess> GetAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult(StaffAccess.None);
        }
    }
}
