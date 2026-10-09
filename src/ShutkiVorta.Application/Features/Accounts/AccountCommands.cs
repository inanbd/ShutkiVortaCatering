using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.Application.Features.Accounts;

public sealed record RegisterCustomerCommand(string FullName, string Email, string Phone, string Password) : IRequest<RegisterResult>;

public sealed record RegisterResult(bool Succeeded, bool RequiresEmailConfirmation, IReadOnlyList<string> Errors);

public sealed record LoginCommand(string Email, string Password, bool RememberMe) : IRequest<SignInOutcome>;

public sealed record LogoutCommand : IRequest;

public sealed record ConfirmEmailCommand(string UserId, string Code) : IRequest<Result>;

public sealed record ResendConfirmationEmailCommand(string Email) : IRequest;

public sealed record ForgotPasswordCommand(string Email) : IRequest;

public sealed record ResetPasswordCommand(string Email, string Code, string Password) : IRequest<Result>;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<Result>, IRequireAuthenticatedUser;

public sealed record GetMyProfileQuery : IRequest<UserAccount?>, IRequireAuthenticatedUser;

public sealed record UpdateMyProfileCommand(
    string FullName,
    string? PhoneNumber,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode) : IRequest<Result>, IRequireAuthenticatedUser;

public sealed class RegisterCustomerCommandValidator : AbstractValidator<RegisterCustomerCommand>
{
    public RegisterCustomerCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Phone).NotEmpty().Must(p => p is not null && p.Count(char.IsDigit) is >= 10 and <= 15)
            .WithMessage("Please enter a valid phone number.");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);
    }
}

public sealed class UpdateMyProfileCommandValidator : AbstractValidator<UpdateMyProfileCommand>
{
    public UpdateMyProfileCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.PhoneNumber).Must(p => string.IsNullOrWhiteSpace(p) || p.Count(char.IsDigit) is >= 10 and <= 15)
            .WithMessage("Please enter a valid phone number.");
        RuleFor(x => x.AddressLine1).MaximumLength(200);
        RuleFor(x => x.AddressLine2).MaximumLength(200);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.State).Must(s => string.IsNullOrWhiteSpace(s) || s.Trim().Length == 2)
            .WithMessage("Please use the 2-letter state code, e.g. TX.");
        RuleFor(x => x.PostalCode).Matches(@"^\d{5}(-\d{4})?$").When(x => !string.IsNullOrWhiteSpace(x.PostalCode))
            .WithMessage("Please enter a valid ZIP code.");
    }
}

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);
    }
}

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(100);
    }
}

internal sealed class AccountCommandHandlers(
    IIdentityService identity,
    ICurrentUser currentUser,
    IEmailTemplateRenderer renderer,
    IEmailService email,
    IAppUrls urls,
    ILogger<AccountCommandHandlers> logger) :
    IRequestHandler<RegisterCustomerCommand, RegisterResult>,
    IRequestHandler<LoginCommand, SignInOutcome>,
    IRequestHandler<LogoutCommand>,
    IRequestHandler<ConfirmEmailCommand, Result>,
    IRequestHandler<ResendConfirmationEmailCommand>,
    IRequestHandler<ForgotPasswordCommand>,
    IRequestHandler<ResetPasswordCommand, Result>,
    IRequestHandler<ChangePasswordCommand, Result>,
    IRequestHandler<GetMyProfileQuery, UserAccount?>,
    IRequestHandler<UpdateMyProfileCommand, Result>
{
    public async Task<RegisterResult> Handle(RegisterCustomerCommand request, CancellationToken cancellationToken)
    {
        var result = await identity.RegisterCustomerAsync(request.FullName.Trim(), request.Email.Trim(), request.Phone.Trim(), request.Password);
        if (!result.Succeeded)
        {
            return new RegisterResult(false, false, result.Errors);
        }

        var userId = result.Value!;
        await SendConfirmationEmailAsync(userId, request.FullName.Trim(), request.Email.Trim(), cancellationToken);

        if (identity.RequireConfirmedEmail)
        {
            return new RegisterResult(true, true, []);
        }

        await identity.SignInAsync(userId, isPersistent: false);
        return new RegisterResult(true, false, []);
    }

    public Task<SignInOutcome> Handle(LoginCommand request, CancellationToken cancellationToken) =>
        identity.PasswordSignInAsync(request.Email.Trim(), request.Password, request.RememberMe);

    public Task Handle(LogoutCommand request, CancellationToken cancellationToken) => identity.SignOutAsync();

    public async Task<Result> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        var result = await identity.ConfirmEmailAsync(request.UserId, request.Code);
        if (result.Succeeded)
        {
            var user = await identity.FindByIdAsync(request.UserId);
            if (user is not null)
            {
                var rendered = renderer.Render(EmailTemplates.Welcome, "স্বাগতম! Welcome to the family", new Dictionary<string, object?>
                {
                    ["CustomerFirstName"] = FirstName(user.FullName),
                    ["MenuUrl"] = urls.Menu(),
                    ["OrdersUrl"] = urls.Absolute("/account/orders"),
                });
                await email.QueueAsync(EmailMessage.Create(user.Email, rendered), cancellationToken);
            }
        }

        return result;
    }

    public async Task Handle(ResendConfirmationEmailCommand request, CancellationToken cancellationToken)
    {
        var user = await identity.FindByEmailAsync(request.Email.Trim());
        if (user is null || user.EmailConfirmed)
        {
            return; // Do not reveal whether an account exists.
        }

        await SendConfirmationEmailAsync(user.Id, user.FullName, user.Email, cancellationToken);
    }

    public async Task Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var emailAddress = request.Email.Trim();
        var code = await identity.GeneratePasswordResetCodeAsync(emailAddress);
        if (code is null)
        {
            logger.LogInformation("Password reset requested for unknown email");
            return; // Do not reveal whether an account exists.
        }

        var user = await identity.FindByEmailAsync(emailAddress);
        var rendered = renderer.Render(EmailTemplates.ResetPassword, "Reset your password", new Dictionary<string, object?>
        {
            ["CustomerFirstName"] = FirstName(user?.FullName),
            ["ResetUrl"] = urls.ResetPassword(emailAddress, code),
        });
        await email.QueueAsync(EmailMessage.Create(emailAddress, rendered) with { Sensitive = true }, cancellationToken);
    }

    public Task<Result> Handle(ResetPasswordCommand request, CancellationToken cancellationToken) =>
        identity.ResetPasswordAsync(request.Email.Trim(), request.Code, request.Password);

    public Task<Result> Handle(ChangePasswordCommand request, CancellationToken cancellationToken) =>
        identity.ChangePasswordAsync(currentUser.UserId!, request.CurrentPassword, request.NewPassword);

    public Task<UserAccount?> Handle(GetMyProfileQuery request, CancellationToken cancellationToken) =>
        identity.FindByIdAsync(currentUser.UserId!);

    public async Task<Result> Handle(UpdateMyProfileCommand request, CancellationToken cancellationToken)
    {
        var result = await identity.UpdateProfileAsync(currentUser.UserId!, new UserProfileUpdate(
            request.FullName.Trim(),
            Clean(request.PhoneNumber),
            Clean(request.AddressLine1),
            Clean(request.AddressLine2),
            Clean(request.City),
            Clean(request.State)?.ToUpperInvariant(),
            Clean(request.PostalCode)));

        if (result.Succeeded)
        {
            await identity.RefreshSignInAsync(currentUser.UserId!);
        }

        return result;
    }

    private async Task SendConfirmationEmailAsync(string userId, string fullName, string emailAddress, CancellationToken cancellationToken)
    {
        var code = await identity.GenerateEmailConfirmationCodeAsync(userId);
        var rendered = renderer.Render(EmailTemplates.ConfirmEmail, "Please confirm your email address", new Dictionary<string, object?>
        {
            ["CustomerFirstName"] = FirstName(fullName),
            ["ConfirmUrl"] = urls.ConfirmEmail(userId, code),
        });
        await email.QueueAsync(EmailMessage.Create(emailAddress, rendered) with { Sensitive = true }, cancellationToken);
    }

    private static string FirstName(string? fullName) =>
        fullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "friend";

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
