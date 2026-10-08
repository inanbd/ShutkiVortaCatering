using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Account;

public sealed class LogoutModel(ISender sender) : AppPageModel(sender)
{
    public IActionResult OnGet() => Redirect("/");

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await Sender.Send(new LogoutCommand(), cancellationToken);
        StatusMessage = "You have been signed out. আবার দেখা হবে!";
        return Redirect("/account/login");
    }
}
