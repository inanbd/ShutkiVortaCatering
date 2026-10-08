using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Domain.Orders;

namespace ShutkiVorta.Web.Pages.Admin.Orders;

public sealed class IndexModel(ISender sender) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public OrderStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public FulfillmentMethod? Fulfillment { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public PagedResult<OrderSummaryDto> Orders { get; private set; } = PagedResult<OrderSummaryDto>.Empty(1, 20);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Orders";
        Orders = await sender.Send(new GetOrdersQuery
        {
            Search = Q,
            Status = Status,
            Fulfillment = Fulfillment,
            ScheduledFrom = From,
            ScheduledTo = To,
            Page = PageNumber,
            PageSize = 25,
        }, cancellationToken);
    }

    public string PageUrl(int page)
    {
        var query = new Dictionary<string, string?>
        {
            ["q"] = Q,
            ["status"] = Status?.ToString(),
            ["fulfillment"] = Fulfillment?.ToString(),
            ["from"] = From?.ToString("yyyy-MM-dd"),
            ["to"] = To?.ToString("yyyy-MM-dd"),
            ["p"] = page.ToString(),
        };
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/admin/orders", query.Where(kv => !string.IsNullOrEmpty(kv.Value)));
    }
}
