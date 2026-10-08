using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Account;

public sealed class ResetPasswordModel(ISender sender) : AppPageModel(sender)
{
    [BindProperty]
    public ResetInput Input { get; set; } = new();

    public IActionResult OnGet(string? email, string? code)
    {
        ViewData["Title"] = "Choose a new password";
        if (string.IsNullOrWhiteSpace(code))
        {
            return Redirect("/account/forgot-password");
        }

        Input.Email = email ?? string.Empty;
        Input.Code = code;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Choose a new password";
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var ok = await TryExecuteAsync(async () =>
        {
            var result = await Sender.Send(new ResetPasswordCommand(Input.Email, Input.Code, Input.Password), cancellationToken);
            if (!result.Succeeded)
            {
                AddErrors(result.Errors);
            }
        }, typeof(ResetInput));

        if (!ok || !ModelState.IsValid)
        {
            return Page();
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
