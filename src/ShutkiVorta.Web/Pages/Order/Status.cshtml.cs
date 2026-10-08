using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Order;

/// <summary>Order status page reached from emailed links (no account needed thanks to the tracking token).</summary>
public sealed class StatusModel(ISender sender, ICurrentUser currentUser) : AppPageModel(sender)
{
    public OrderDetailsDto Order { get; private set; } = null!;

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    public async Task<IActionResult> OnGetAsync(string orderNumber, CancellationToken cancellationToken)
    {
        ViewData["Title"] = $"Order {orderNumber}";
        var order = await LoadAsync(orderNumber, cancellationToken);
        if (order is null)
        {
            return currentUser.IsAuthenticated && string.IsNullOrEmpty(Token)
                ? Redirect($"/account/orders/{Uri.EscapeDataString(orderNumber)}")
                : NotFound();
        }

        Order = order;
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(string orderNumber, string? reason, CancellationToken cancellationToken)
    {
        var ok = await TryExecuteAsync(() => Sender.Send(new CancelOrderByCustomerCommand(orderNumber, Token, reason), cancellationToken));
        if (ok)
        {
            StatusMessage = "Your order has been cancelled. We hope to cook for you another time!";
        }
        else
        {
            ErrorMessage = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        }

        return Redirect($"/order/{Uri.EscapeDataString(orderNumber)}?token={Uri.EscapeDataString(Token ?? string.Empty)}");
    }

    private async Task<OrderDetailsDto?> LoadAsync(string orderNumber, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(Token) ? null : await Sender.Send(new GetOrderByTrackingTokenQuery(orderNumber, Token), cancellationToken);
}
