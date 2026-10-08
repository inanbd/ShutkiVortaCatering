using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Features.Settings;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Settings;

public sealed class IndexModel(ISender sender, ICurrentUser currentUser) : AppPageModel(sender)
{
    public SettingsOverviewDto Settings { get; private set; } = null!;

    [BindProperty]
    public string? TestEmailTo { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Settings";
        TestEmailTo = currentUser.Email;
        Settings = await Sender.Send(new GetSettingsOverviewQuery(), cancellationToken);
    }

    public async Task<IActionResult> OnPostTestEmailAsync(CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new SendTestEmailCommand(TestEmailTo ?? string.Empty), cancellationToken);
        if (result.Succeeded)
        {
            StatusMessage = $"Test email sent to {TestEmailTo}.";
        }
        else
        {
            ErrorMessage = string.Join(" ", result.Errors);
        }

        return RedirectToPage();
    }
}
