using FluentValidation;
using MediatR;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Application.Features.Orders;
using ValidationException = ShutkiVorta.Application.Common.Exceptions.ValidationException;

namespace ShutkiVorta.Application.Features.Staff;

// Adding people, their invitations and their details. Admins only: they decide who may get into the admin area.

/// <summary>
/// Adds someone to the staff. An email that already holds a staff role is refused (change the role on their page). An
/// existing account with a confirmed email is added once the admin has confirmed who it is (<see cref="ConfirmedUserId"/>):
/// they keep their password and are told by email. An account whose email was never confirmed is taken over (its password
/// removed) and invited, so whoever registered the address cannot get in with their own password. Otherwise a new account
/// is created and invited. Invited people get in once they have set their password through the link.
/// </summary>
/// <param name="ConfirmedUserId">The existing account the admin confirmed adding (from the confirmation step).</param>
public sealed record AddStaffMemberCommand(
    string FullName,
    string Email,
    string? PhoneNumber,
    StaffRole Role,
    string? JobTitle,
    string? ConfirmedUserId = null) : IRequest<AddStaffMemberResult>, IRequireAdmin;

/// <summary>Gives a former staff member a role again: invited again unless they can already sign in.</summary>
public sealed record ReAddStaffMemberCommand(string UserId, StaffRole Role) : IRequest<StaffInvitationDto>, IRequireAdmin;

/// <summary>A new invitation link for someone who has not set their password yet; earlier links stop working.</summary>
public sealed record ResendStaffInvitationCommand(string UserId) : IRequest<StaffInvitationDto>, IRequireAdmin;

/// <summary>Fixes the email of someone who has not set their password yet (e.g. a typo) and invites them at the new address.</summary>
public sealed record ChangeStaffEmailCommand(string UserId, string Email) : IRequest<StaffInvitationDto>, IRequireAdmin;

/// <summary>Name, phone and job title. <see cref="Version"/> is the person's StaffProfiles.Version as the page showed it.</summary>
public sealed record UpdateStaffDetailsCommand(string UserId, string FullName, string? PhoneNumber, string? JobTitle, int Version)
    : IRequest, IRequireAdmin;

/// <summary>Lets someone sign in again after too many wrong passwords.</summary>
public sealed record UnlockStaffSignInCommand(string UserId) : IRequest, IRequireAdmin;

/// <summary>
/// Takes away someone's staff role. They keep their account and their shifts; an open shift is closed now and flagged for
/// review; an invitation they have not used stops working.
/// </summary>
public sealed record RemoveFromStaffCommand(string UserId, int Version) : IRequest, IRequireAdmin;

public enum AddStaffMemberOutcome
{
    /// <summary>Added: see <see cref="AddStaffMemberResult.Invitation"/>.</summary>
    Added = 1,

    /// <summary>The email belongs to someone on the staff already: nothing changed.</summary>
    AlreadyStaff = 2,

    /// <summary>The email belongs to an existing account with a confirmed email: ask the admin to confirm it is the right person.</summary>
    ConfirmExistingAccount = 3,
}

public sealed class AddStaffMemberResult
{
    public AddStaffMemberOutcome Outcome { get; init; }

    /// <summary>Set when <see cref="Outcome"/> is Added.</summary>
    public StaffInvitationDto? Invitation { get; init; }

    /// <summary>Who the email belongs to, when it belongs to an existing account.</summary>
    public ExistingAccountDto? ExistingAccount { get; init; }
}

/// <summary>An existing account, described so the admin can tell whether it is the person they mean.</summary>
public sealed class ExistingAccountDto
{
    public string UserId { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public int OrderCount { get; init; }

    /// <summary>Their staff role (when the email is already staff).</summary>
    public StaffRole? Role { get; init; }

    public bool IsFormerStaff { get; init; }
}

internal static class StaffInputRules
{
    public const int MaxNameLength = 120;
    public const int MaxEmailLength = 256;
    public const int MaxJobTitleLength = 100;

    public static void ValidName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Please enter their name.").MaximumLength(MaxNameLength);

    public static void ValidEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Please enter their email address.").EmailAddress().MaximumLength(MaxEmailLength);

    public static void ValidPhone<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(p => string.IsNullOrWhiteSpace(p) || p.Count(char.IsDigit) is >= 10 and <= 15).WithMessage("Please enter a valid phone number.");

    public static void ValidJobTitle<T>(this IRuleBuilder<T, string?> rule) => rule.MaximumLength(MaxJobTitleLength);

    public static void ValidRole<T>(this IRuleBuilder<T, StaffRole> rule) => rule.IsInEnum().WithMessage("Choose Admin, Manager or Employee.");
}

public sealed class AddStaffMemberCommandValidator : AbstractValidator<AddStaffMemberCommand>
{
    public AddStaffMemberCommandValidator()
    {
        RuleFor(x => x.FullName).ValidName();
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.PhoneNumber).ValidPhone();
        RuleFor(x => x.Role).ValidRole();
        RuleFor(x => x.JobTitle).ValidJobTitle();
    }
}

public sealed class ReAddStaffMemberCommandValidator : AbstractValidator<ReAddStaffMemberCommand>
{
    public ReAddStaffMemberCommandValidator() => RuleFor(x => x.Role).ValidRole();
}

public sealed class ChangeStaffEmailCommandValidator : AbstractValidator<ChangeStaffEmailCommand>
{
    public ChangeStaffEmailCommandValidator() => RuleFor(x => x.Email).ValidEmail();
}

public sealed class UpdateStaffDetailsCommandValidator : AbstractValidator<UpdateStaffDetailsCommand>
{
    public UpdateStaffDetailsCommandValidator()
    {
        RuleFor(x => x.FullName).ValidName();
        RuleFor(x => x.PhoneNumber).ValidPhone();
        RuleFor(x => x.JobTitle).ValidJobTitle();
    }
}

internal sealed class StaffMemberCommandHandlers(
    IIdentityService identity,
    IStaffDirectory directory,
    IStaffPermissionStore store,
    IStaffAuditLog audit,
    IOrderRepository orders,
    IEmailTemplateRenderer renderer,
    IEmailService email,
    IAppUrls urls,
    ICurrentUser currentUser,
    IDateTimeProvider clock) :
    IRequestHandler<AddStaffMemberCommand, AddStaffMemberResult>,
    IRequestHandler<ReAddStaffMemberCommand, StaffInvitationDto>,
    IRequestHandler<ResendStaffInvitationCommand, StaffInvitationDto>,
    IRequestHandler<ChangeStaffEmailCommand, StaffInvitationDto>,
    IRequestHandler<UpdateStaffDetailsCommand>,
    IRequestHandler<UnlockStaffSignInCommand>,
    IRequestHandler<RemoveFromStaffCommand>
{
    private string By => currentUser.Email ?? "Admin";

    public async Task<AddStaffMemberResult> Handle(AddStaffMemberCommand request, CancellationToken cancellationToken)
    {
        var fullName = request.FullName.Trim();
        var emailAddress = request.Email.Trim();
        var phone = Clean(request.PhoneNumber);

        var existing = await identity.FindByEmailAsync(emailAddress);
        if (existing is null)
        {
            var created = await identity.CreateStaffAccountAsync(fullName, emailAddress, phone);
            if (!created.Succeeded)
            {
                throw new ValidationException(nameof(AddStaffMemberCommand.Email), string.Join(" ", created.Errors));
            }

            await JoinStaffAsync(created.Value!, request.Role, request.JobTitle, cancellationToken);
            return Added(await InviteAsync(created.Value!, fullName, emailAddress, request.Role, note: null, cancellationToken));
        }

        // Never a second way to change someone's role: that has its own page, with its own checks and wording.
        var member = await directory.GetAsync(existing.Id, cancellationToken);
        var account = await DescribeAsync(existing, member, cancellationToken);
        if (member?.Role is not null)
        {
            return new AddStaffMemberResult { Outcome = AddStaffMemberOutcome.AlreadyStaff, ExistingAccount = account };
        }

        if (existing.EmailConfirmed && existing.HasPassword)
        {
            // Someone who proved they own the address: they keep their password, once the admin confirms it is the right person.
            if (!string.Equals(request.ConfirmedUserId, existing.Id, StringComparison.Ordinal))
            {
                return new AddStaffMemberResult { Outcome = AddStaffMemberOutcome.ConfirmExistingAccount, ExistingAccount = account };
            }

            await JoinStaffAsync(existing.Id, request.Role, request.JobTitle, cancellationToken);
            return Added(await TellAddedAsync(existing.Id, existing.FullName, existing.Email, request.Role, cancellationToken));
        }

        // Nobody has proved they own this address, so whoever registered it must not get in with their own password: the
        // account starts over as an invitation (its password removed, its sessions and links ended).
        Check(await identity.ResetToInvitationAsync(existing.Id));
        Check(await identity.UpdateContactAsync(existing.Id, fullName, phone ?? existing.PhoneNumber));
        await JoinStaffAsync(existing.Id, request.Role, request.JobTitle, cancellationToken);
        var note = existing.EmailConfirmed ? null : "The account's email was never confirmed, so its password was removed.";
        return Added(await InviteAsync(existing.Id, fullName, existing.Email, request.Role, note, cancellationToken));
    }

    public async Task<StaffInvitationDto> Handle(ReAddStaffMemberCommand request, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(request.UserId, cancellationToken);
        if (member.Role is { } role)
        {
            throw new ValidationException($"{member.FullName} is already on the staff as {role.DisplayName()}.");
        }

        var account = await identity.FindByIdAsync(member.UserId) ?? throw new NotFoundException("Staff member", request.UserId);
        Check(await store.SetStaffRoleAsync(member.UserId, request.Role, ActorId, By, cancellationToken));
        if (account.EmailConfirmed && account.HasPassword)
        {
            return await TellAddedAsync(member.UserId, member.FullName, member.Email, request.Role, cancellationToken);
        }

        Check(await identity.ResetToInvitationAsync(member.UserId));
        return await InviteAsync(member.UserId, member.FullName, member.Email, request.Role, note: null, cancellationToken);
    }

    public async Task<StaffInvitationDto> Handle(ResendStaffInvitationCommand request, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(request.UserId, cancellationToken);
        var role = member.Role ?? throw new ValidationException($"{member.FullName} is not on the staff. Add them again first.");
        if (member.HasPassword && member.EmailConfirmed)
        {
            throw new ValidationException(
                $"{member.FullName} has already set a password. If they forgot it, they can use \"Forgot password?\" on the sign-in page.");
        }

        // A new stamp: the links sent before stop working, so only the newest invitation can be used.
        Check(await identity.ResetToInvitationAsync(member.UserId));
        return await InviteAsync(member.UserId, member.FullName, member.Email, role, note: null, cancellationToken);
    }

    public async Task<StaffInvitationDto> Handle(ChangeStaffEmailCommand request, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(request.UserId, cancellationToken);
        var role = member.Role ?? throw new ValidationException($"{member.FullName} is not on the staff. Add them again first.");
        if (member.HasPassword)
        {
            throw new ValidationException($"{member.FullName} has already set a password, so their email can't be changed here.");
        }

        var newEmail = request.Email.Trim();
        if (string.Equals(newEmail, member.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(nameof(ChangeStaffEmailCommand.Email), "That is already their email address.");
        }

        var changed = await identity.ChangeEmailWithoutPasswordAsync(member.UserId, newEmail);
        if (!changed.Succeeded)
        {
            throw new ValidationException(nameof(ChangeStaffEmailCommand.Email), string.Join(" ", changed.Errors));
        }

        await audit.WriteAsync(new StaffAuditEntry(member.UserId, member.FullName, StaffAuditActions.EmailChanged,
            $"{member.Email} → {newEmail}. The earlier invitation link stopped working.", By), cancellationToken);
        return await InviteAsync(member.UserId, member.FullName, newEmail, role, note: null, cancellationToken);
    }

    public async Task Handle(UpdateStaffDetailsCommand request, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(request.UserId, cancellationToken);
        await StaffVersions.CheckAsync(store, member.UserId, request.Version, cancellationToken);

        var fullName = request.FullName.Trim();
        var phone = Clean(request.PhoneNumber);
        Check(await identity.UpdateContactAsync(member.UserId, fullName, phone));
        await store.UpdateProfileAsync(member.UserId, request.JobTitle, By, cancellationToken);

        var changes = new List<string>();
        if (fullName != member.FullName)
        {
            changes.Add($"Name: {member.FullName} → {fullName}.");
        }

        if (phone != Clean(member.PhoneNumber))
        {
            changes.Add(phone is null ? "Phone number removed." : $"Phone: {phone}.");
        }

        if (changes.Count > 0)
        {
            await audit.WriteAsync(new StaffAuditEntry(member.UserId, fullName, StaffAuditActions.ProfileChanged, string.Join(" ", changes), By), cancellationToken);
        }
    }

    public async Task Handle(UnlockStaffSignInCommand request, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(request.UserId, cancellationToken);
        if (!member.IsLockedOut)
        {
            throw new ValidationException($"{member.FullName} isn't locked out: they can sign in.");
        }

        Check(await identity.UnlockAsync(member.UserId));
        await audit.WriteAsync(new StaffAuditEntry(member.UserId, member.FullName, StaffAuditActions.AccountUnlocked,
            "Sign-in unlocked after too many wrong passwords.", By), cancellationToken);
    }

    public async Task Handle(RemoveFromStaffCommand request, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(request.UserId, cancellationToken);
        if (member.Role is null)
        {
            throw new ValidationException($"{member.FullName} is not on the staff.");
        }

        await StaffVersions.CheckAsync(store, member.UserId, request.Version, cancellationToken);
        Check(await store.SetStaffRoleAsync(member.UserId, null, ActorId, By, cancellationToken));
        if (!member.HasPassword)
        {
            // An invitation that was never used must not let them in later (e.g. if they are added again).
            Check(await identity.RotateSecurityStampAsync(member.UserId));
        }
    }

    private string ActorId => currentUser.UserId ?? throw new ForbiddenAccessException();

    private async Task<StaffMemberDto> RequireMemberAsync(string userId, CancellationToken cancellationToken) =>
        await directory.GetAsync(userId.Trim(), cancellationToken) ?? throw new NotFoundException("Staff member", userId);

    /// <summary>Gives the person their role (through the store, with its checks and audit entry) and their job title.</summary>
    private async Task JoinStaffAsync(string userId, StaffRole role, string? jobTitle, CancellationToken cancellationToken)
    {
        Check(await store.SetStaffRoleAsync(userId, role, ActorId, By, cancellationToken));
        if (Clean(jobTitle) is { } title)
        {
            await store.UpdateProfileAsync(userId, title, By, cancellationToken);
        }
    }

    private async Task<ExistingAccountDto> DescribeAsync(UserAccount account, StaffMemberDto? member, CancellationToken cancellationToken)
    {
        var counts = await orders.CountByCustomerAsync([account.Id], cancellationToken);
        return new ExistingAccountDto
        {
            UserId = account.Id,
            FullName = account.FullName,
            Email = account.Email,
            CreatedAtUtc = account.CreatedAtUtc,
            OrderCount = counts.GetValueOrDefault(account.Id),
            Role = member?.Role,
            IsFormerStaff = member is { IsFormerStaff: true },
        };
    }

    /// <summary>
    /// Creates the set-password link and emails it, unless the website address is not set: a link built from the request's
    /// address could point anywhere, so then it is only shown to the admin. The link itself is never stored or logged.
    /// </summary>
    private async Task<StaffInvitationDto> InviteAsync(
        string userId, string fullName, string emailAddress, StaffRole role, string? note, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var code = await identity.GenerateStaffInvitationCodeAsync(userId);
        var link = urls.StaffInvitation(emailAddress, code);
        var emailed = urls.HasConfiguredBaseUrl;

        if (emailed)
        {
            var rendered = renderer.Render(EmailTemplates.StaffInvitation, StaffInvitations.InvitationSubject, new Dictionary<string, object?>
            {
                ["FirstName"] = FirstName(fullName),
                ["InvitedBy"] = currentUser.Name ?? "An admin",
                ["RoleName"] = role.DisplayName(),
                ["InvitationUrl"] = link,
                ["ValidForDays"] = StaffInvitations.ValidFor.Days,
                ["ExpiresOn"] = Format.Date(clock.ToBusinessTime(now + StaffInvitations.ValidFor)),
            });
            await email.QueueAsync(EmailMessage.Create(emailAddress, rendered) with { Sensitive = true }, cancellationToken);
        }

        var sent = emailed
            ? $"Invitation emailed to {emailAddress}; the link works for {StaffInvitations.ValidFor.Days} days."
            : $"Invitation link created for {emailAddress}. Not emailed: the website address isn't set.";
        await audit.WriteAsync(new StaffAuditEntry(userId, fullName, StaffAuditActions.InvitationSent,
            note is null ? sent : $"{note} {sent}", By), cancellationToken);

        return new StaffInvitationDto
        {
            UserId = userId,
            FullName = fullName,
            Email = emailAddress,
            Role = role,
            Kind = StaffInvitationKind.Invited,
            Link = link,
            Emailed = emailed,
            CreatedAtUtc = now,
        };
    }

    /// <summary>An existing account was added: no link to send, they sign in as usual. They are told, so it never comes as a surprise.</summary>
    private async Task<StaffInvitationDto> TellAddedAsync(
        string userId, string fullName, string emailAddress, StaffRole role, CancellationToken cancellationToken)
    {
        var rendered = renderer.Render(EmailTemplates.StaffAdded, StaffInvitations.AddedSubject, new Dictionary<string, object?>
        {
            ["FirstName"] = FirstName(fullName),
            ["AddedBy"] = currentUser.Name ?? "An admin",
            ["RoleName"] = role.DisplayName(),
            ["SignInUrl"] = urls.Login(),
        });
        await email.QueueAsync(EmailMessage.Create(emailAddress, rendered), cancellationToken);

        return new StaffInvitationDto
        {
            UserId = userId,
            FullName = fullName,
            Email = emailAddress,
            Role = role,
            Kind = StaffInvitationKind.Added,
            Emailed = true,
            CreatedAtUtc = clock.UtcNow,
        };
    }

    private static AddStaffMemberResult Added(StaffInvitationDto invitation) =>
        new() { Outcome = AddStaffMemberOutcome.Added, Invitation = invitation };

    private static void Check(Common.Models.Result result)
    {
        if (!result.Succeeded)
        {
            throw new ValidationException(string.Join(" ", result.Errors));
        }
    }

    private static string FirstName(string fullName) =>
        fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Optimistic concurrency for a person's page: every form posts the StaffProfiles.Version it was shown.</summary>
internal static class StaffVersions
{
    public const string ChangedMeanwhile =
        "Someone changed this person while you were looking at them. Reload the page to see the latest and try again.";

    public static async Task CheckAsync(IStaffPermissionStore store, string userId, int version, CancellationToken cancellationToken)
    {
        if ((await store.GetUserPermissionsAsync(userId, cancellationToken)).Version != version)
        {
            throw new ValidationException(ChangedMeanwhile);
        }
    }
}
