using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Orders;

public sealed class DetailsModel(ISender sender) : AppPageModel(sender)
{
    public OrderDetailsDto Order { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string orderNumber, CancellationToken cancellationToken)
    {
        var order = await Sender.Send(new GetOrderDetailsQuery(orderNumber), cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        Order = order;
        ViewData["Title"] = $"Order {order.OrderNumber}";
        return Page();
    }

    public async Task<IActionResult> OnPostStatusAsync(string orderNumber, OrderStatus newStatus, string? note, bool notifyCustomer, CancellationToken cancellationToken)
    {
        var ok = await TryExecuteAsync(() => Sender.Send(new UpdateOrderStatusCommand(orderNumber, newStatus, note, notifyCustomer), cancellationToken));
        if (ok)
        {
            StatusMessage = $"Order marked as {newStatus.DisplayName().ToLowerInvariant()}{(notifyCustomer ? " and the customer was notified by email" : string.Empty)}.";
        }
        else
        {
            ErrorMessage = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        }

        return Redirect($"/admin/orders/{Uri.EscapeDataString(orderNumber)}");
    }

    public async Task<IActionResult> OnPostNotesAsync(string orderNumber, string? adminNotes, CancellationToken cancellationToken)
    {
        var ok = await TryExecuteAsync(() => Sender.Send(new SaveOrderAdminNotesCommand(orderNumber, adminNotes), cancellationToken));
        if (ok)
        {
            StatusMessage = "Notes saved.";
        }
        else
        {
            ErrorMessage = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        }

        return Redirect($"/admin/orders/{Uri.EscapeDataString(orderNumber)}");
    }
}
