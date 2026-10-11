using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Web.Infrastructure;
using ShutkiVorta.Web.Infrastructure.Security;

namespace ShutkiVorta.Web.Pages.Account;

public sealed class LoginModel(ISender sender, ICurrentStaff currentStaff, IStaffAccessService staffAccess) : AppPageModel(sender)
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool ShowResendLink { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Sign in";
        return User.Identity?.IsAuthenticated == true
            ? LocalRedirect(SafeReturnUrl(ReturnUrl) ?? Landing(await currentStaff.GetAsync(cancellationToken)))
            : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Sign in";
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await Sender.Send(new LoginCommand(Input.Email, Input.Password, Input.RememberMe), cancellationToken);
        switch (result.Outcome)
        {
            case SignInOutcome.Succeeded:
                // This request still sees an anonymous user, so the person's staff access is read by their id.
                return LocalRedirect(SafeReturnUrl(ReturnUrl) ?? Landing(await staffAccess.GetAsync(result.UserId!, cancellationToken)));
            case SignInOutcome.LockedOut:
                ModelState.AddModelError(string.Empty, "Too many failed attempts. Your account is locked for 15 minutes — or reset your password.");
                break;
            case SignInOutcome.EmailNotConfirmed:
                ModelState.AddModelError(string.Empty, "Please confirm your email address before signing in. Check your inbox for our email.");
                ShowResendLink = true;
                break;
            default:
                ModelState.AddModelError(string.Empty, "Invalid email or password.");
                break;
        }

        return Page();
    }

    /// <summary>Staff start on their admin landing page (dashboard or time clock); customers on their orders.</summary>
    private static string Landing(StaffAccess access) => access.IsStaff ? StaffLanding.For(access) : "/account/orders";

    public sealed class LoginInput
    {
        [Required, EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Keep me signed in")]
        public bool RememberMe { get; set; } = true;
    }
}
