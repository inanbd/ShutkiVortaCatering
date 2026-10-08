using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Account.Orders;

public sealed class DetailsModel(ISender sender) : AppPageModel(sender)
{
    public OrderDetailsDto Order { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string orderNumber, CancellationToken cancellationToken)
    {
        var order = await Sender.Send(new GetMyOrderQuery(orderNumber), cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        Order = order;
        ViewData["Title"] = $"Order {order.OrderNumber}";
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(string orderNumber, string? reason, CancellationToken cancellationToken)
    {
        var order = await Sender.Send(new GetMyOrderQuery(orderNumber), cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        var ok = await TryExecuteAsync(() => Sender.Send(new CancelOrderByCustomerCommand(order.OrderNumber, order.TrackingToken, reason), cancellationToken));
        if (ok)
        {
            StatusMessage = "Your order has been cancelled.";
        }
        else
        {
            ErrorMessage = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        }

        return Redirect($"/account/orders/{Uri.EscapeDataString(order.OrderNumber)}");
    }
}
