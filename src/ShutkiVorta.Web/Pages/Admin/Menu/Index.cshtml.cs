using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Menu;

public sealed class IndexModel(ISender sender) : AppPageModel(sender)
{
    public IReadOnlyList<MenuItemDto> Items { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Menu items";
        Items = await Sender.Send(new GetAdminMenuQuery(), cancellationToken);
    }

    public async Task<IActionResult> OnPostAvailabilityAsync(int id, bool isAvailable, CancellationToken cancellationToken)
    {
        await Sender.Send(new SetMenuItemAvailabilityCommand(id, isAvailable), cancellationToken);
        StatusMessage = isAvailable ? "Item is now available on the menu." : "Item hidden from the menu.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        var ok = await TryExecuteAsync(() => Sender.Send(new DeleteMenuItemCommand(id), cancellationToken));
        if (ok)
        {
            StatusMessage = "Menu item deleted. The sitemap and robots.txt were updated automatically.";
        }
        else
        {
            ErrorMessage = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        }

        return RedirectToPage();
    }
}
