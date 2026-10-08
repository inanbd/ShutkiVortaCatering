using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Account;

public sealed class ForgotPasswordModel(ISender sender) : AppPageModel(sender)
{
    [BindProperty]
    [Required, EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    public bool Sent { get; private set; }

    public void OnGet() => ViewData["Title"] = "Forgot password";

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Forgot password";
        if (!ModelState.IsValid)
        {
            return Page();
        }

        await Sender.Send(new ForgotPasswordCommand(Email), cancellationToken);
        Sent = true;
        return Page();
    }
}
