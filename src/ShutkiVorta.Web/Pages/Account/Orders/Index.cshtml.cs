using MediatR;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShutkiVorta.Application.Features.Orders;

namespace ShutkiVorta.Web.Pages.Account.Orders;

public sealed class IndexModel(ISender sender) : PageModel
{
    public IReadOnlyList<OrderSummaryDto> Orders { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "My orders";
        Orders = await sender.Send(new GetMyOrdersQuery(), cancellationToken);
    }
}
