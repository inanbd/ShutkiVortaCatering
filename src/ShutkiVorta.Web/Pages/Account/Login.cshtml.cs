using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Account;

public sealed class LoginModel(ISender sender) : AppPageModel(sender)
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool ShowResendLink { get; private set; }

    public IActionResult OnGet()
    {
        ViewData["Title"] = "Sign in";
        return User.Identity?.IsAuthenticated == true ? LocalRedirect(SafeReturnUrl(ReturnUrl) ?? "/account/orders") : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Sign in";
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var outcome = await Sender.Send(new LoginCommand(Input.Email, Input.Password, Input.RememberMe), cancellationToken);
        switch (outcome)
        {
            case SignInOutcome.Succeeded:
                return LocalRedirect(SafeReturnUrl(ReturnUrl) ?? "/account/orders");
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
