using FluentValidation;
using MediatR;
using ShutkiVorta.Application.Common.Behaviors;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.UnitTests.TestDoubles;
using ValidationException = ShutkiVorta.Application.Common.Exceptions.ValidationException;

namespace ShutkiVorta.UnitTests.Application;

public sealed class PipelineBehaviorTests
{
    private sealed record AdminRequest : IRequest<string>, IRequireAdmin;

    private sealed record CustomerRequest : IRequest<string>, IRequireAuthenticatedUser;

    private sealed record NamedRequest(string Name) : IRequest<string>;

    private sealed class NamedRequestValidator : AbstractValidator<NamedRequest>
    {
        public NamedRequestValidator() => RuleFor(x => x.Name).NotEmpty();
    }

    private static Task<string> Ok(CancellationToken _) => Task.FromResult("ok");

    [Fact]
    public async Task Authorization_BlocksNonAdmins()
    {
        var behavior = new AuthorizationBehavior<AdminRequest, string>(new FakeCurrentUser { UserId = "u1" });
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => behavior.Handle(new AdminRequest(), Ok, CancellationToken.None));
    }

    [Fact]
    public async Task Authorization_AllowsAdmins()
    {
        var behavior = new AuthorizationBehavior<AdminRequest, string>(new FakeCurrentUser { UserId = "u1", IsAdmin = true });
        Assert.Equal("ok", await behavior.Handle(new AdminRequest(), Ok, CancellationToken.None));
    }

    [Fact]
    public async Task Authorization_RequiresSignInForCustomerRequests()
    {
        var behavior = new AuthorizationBehavior<CustomerRequest, string>(new FakeCurrentUser());
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => behavior.Handle(new CustomerRequest(), Ok, CancellationToken.None));
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
}
