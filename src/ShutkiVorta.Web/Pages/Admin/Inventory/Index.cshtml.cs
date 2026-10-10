using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Features.Inventory;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Inventory;

public sealed class IndexModel(ISender sender) : AppPageModel(sender)
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public InventoryOverviewDto Overview { get; private set; } =
        new(new InventoryPurchaseList(PagedResult<InventoryPurchaseSummaryDto>.Empty(1, 25), 0m), 0m, 0m, 0);

    public bool IsFiltered => !string.IsNullOrWhiteSpace(Q) || From is not null || To is not null;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Inventory";
        Overview = await Sender.Send(new GetInventoryOverviewQuery { Search = Q, From = From, To = To, Page = PageNumber }, cancellationToken);
    }

    public string PageUrl(int page)
    {
        var query = new Dictionary<string, string?>
        {
            ["q"] = Q,
            ["from"] = From?.ToString("yyyy-MM-dd"),
            ["to"] = To?.ToString("yyyy-MM-dd"),
            ["p"] = page.ToString(),
        };
        return QueryHelpers.AddQueryString("/admin/inventory", query.Where(kv => !string.IsNullOrEmpty(kv.Value)));
    }
}
