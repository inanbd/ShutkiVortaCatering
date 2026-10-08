using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Account;

public sealed class ConfirmEmailModel(ISender sender) : AppPageModel(sender)
{
    public bool Succeeded { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? userId, string? code, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Confirm email";
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        {
            return Redirect("/");
        }

        var result = await Sender.Send(new ConfirmEmailCommand(userId, code), cancellationToken);
        Succeeded = result.Succeeded;
        return Page();
    }
}
