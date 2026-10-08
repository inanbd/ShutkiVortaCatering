using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Orders;

namespace ShutkiVorta.Web.Pages.Order;

public sealed class ThankYouModel(ISender sender, IOptions<OrderingOptions> ordering, IOptions<BusinessOptions> business) : PageModel
{
    public OrderDetailsDto Order { get; private set; } = null!;
    public OrderingOptions Ordering => ordering.Value;
    public BusinessOptions Business => business.Value;

    public async Task<IActionResult> OnGetAsync(string orderNumber, string? token, CancellationToken cancellationToken)
    {
        var order = string.IsNullOrWhiteSpace(token) ? null : await sender.Send(new GetOrderByTrackingTokenQuery(orderNumber, token), cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        Order = order;
        ViewData["Title"] = "Thank you for your order";
        return Page();
    }
}
