using MediatR;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShutkiVorta.Application.Features.Wholesale;

namespace ShutkiVorta.Web.Pages.Account.RestaurantOrders;

public sealed class IndexModel(ISender sender) : PageModel
{
    public IReadOnlyList<StandingOrderSummaryDto> StandingOrders { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Restaurant orders";
        StandingOrders = await sender.Send(new GetMyStandingOrdersQuery(), cancellationToken);
    }
}
