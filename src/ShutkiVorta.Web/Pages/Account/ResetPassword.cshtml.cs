using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Web.Infrastructure;
using ShutkiVorta.Web.Infrastructure.Security;

namespace ShutkiVorta.Web.Pages.Account;

/// <summary>
/// Choosing a new password from an emailed link. With <c>?invite=1</c> it is a staff invitation: the person sets their first
/// password, is signed in and goes straight to their landing page (the time clock for Employees).
/// </summary>
public sealed class ResetPasswordModel(ISender sender, IIdentityService identity, IStaffAccessService staffAccess) : AppPageModel(sender)
{
    [BindProperty]
    public ResetInput Input { get; set; } = new();

    /// <summary>"1" for a staff invitation link (?invite=1) rather than a password reset.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Invite { get; set; }

    public bool IsInvitation => Invite is "1" or "true";

    /// <summary>The invitation can no longer be used: the form is replaced by what to do instead.</summary>
    public bool InvitationExpired { get; private set; }

    public string Heading => IsInvitation ? "Set your password" : "Choose a new password";

    public async Task<IActionResult> OnGetAsync(string? email, string? code)
    {
        ViewData["Title"] = Heading;
        if (string.IsNullOrWhiteSpace(code))
        {
            return Redirect("/account/forgot-password");
        }

        Input.Email = email ?? string.Empty;
        Input.Code = code;

        // Say so before they type a password twice (the code is only checked here, not used up).
        InvitationExpired = IsInvitation && !await identity.IsStaffInvitationValidAsync(Input.Email, code);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = Heading;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        string? signedInUserId = null;
        var ok = await TryExecuteAsync(async () =>
        {
            var result = await Sender.Send(new ResetPasswordCommand(Input.Email, Input.Code, Input.Password, IsInvitation), cancellationToken);
            if (!result.Succeeded)
            {
                InvitationExpired = IsInvitation && result.Errors.Contains(StaffInvitations.ExpiredOrUsed);
                AddErrors(result.Errors.Where(e => !InvitationExpired || e != StaffInvitations.ExpiredOrUsed));
            }

            signedInUserId = result.Value;
        }, typeof(ResetInput));

        if (!ok || !ModelState.IsValid || InvitationExpired)
        {
            return Page();
        }

        if (IsInvitation && signedInUserId is not null)
        {
            // This request still sees an anonymous user, so the person's staff access is read by their id.
            var access = await staffAccess.GetAsync(signedInUserId, cancellationToken);
            if (access.IsStaff)
            {
                StatusMessage = "Welcome to the team! Your password is set and you're signed in. When you clock in, your phone will ask " +
                                "for your location: it is saved only when you clock in or out.";
                return LocalRedirect(StaffLanding.For(access));
            }

            StatusMessage = "Your password is set and you're signed in.";
            return LocalRedirect("/account/orders");
        }

        StatusMessage = "Your password has been changed. Please sign in with your new password.";
        return Redirect("/account/login");
    }

    public sealed class ResetInput
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Code { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        [Display(Name = "Confirm new password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
