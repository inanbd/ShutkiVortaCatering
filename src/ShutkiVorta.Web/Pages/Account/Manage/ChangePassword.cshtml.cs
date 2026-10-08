using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Account.Manage;

public sealed class ChangePasswordModel(ISender sender) : AppPageModel(sender)
{
    [BindProperty]
    public PasswordInput Input { get; set; } = new();

    public void OnGet() => ViewData["Title"] = "Change password";

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Change password";
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var ok = await TryExecuteAsync(async () =>
        {
            var result = await Sender.Send(new ChangePasswordCommand(Input.CurrentPassword, Input.NewPassword), cancellationToken);
            if (!result.Succeeded)
            {
                AddErrors(result.Errors);
            }
        }, typeof(PasswordInput));

        if (!ok || !ModelState.IsValid)
        {
            return Page();
        }

        StatusMessage = "Your password has been changed.";
        return RedirectToPage();
    }

    public sealed class PasswordInput
    {
        [Required, DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
        [Display(Name = "Confirm new password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
