using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Web.Infrastructure;
using ShutkiVorta.Web.Services;

namespace ShutkiVorta.Web.Pages;

public sealed class CartModel(ISender sender, CartService cart) : AppPageModel(sender)
{
    public CartQuoteDto Quote { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Your order";
        Quote = await Sender.Send(new PriceCartQuery(cart.Lines), cancellationToken);

        // Silently drop items that were removed from the menu.
        cart.RemoveMany(Quote.MissingItemIds);
    }

    public IActionResult OnPostAdd(int menuItemId, string? quantity, string? returnUrl)
    {
        var qty = ParseQuantity(quantity);
        if (menuItemId > 0 && qty > 0)
        {
            cart.Add(menuItemId, qty);
            StatusMessage = "Added to your order. ধন্যবাদ!";
        }

        return LocalRedirect(SafeReturnUrl(returnUrl) is { } url && !url.StartsWith("/cart", StringComparison.OrdinalIgnoreCase) ? url : "/cart");
    }

    public IActionResult OnPostUpdate(int menuItemId, string? quantity)
    {
        cart.SetQuantity(menuItemId, ParseQuantity(quantity));
        return RedirectToPage();
    }

    public IActionResult OnPostRemove(int menuItemId)
    {
        cart.Remove(menuItemId);
        StatusMessage = "Item removed from your order.";
        return RedirectToPage();
    }

    private static decimal ParseQuantity(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var q) ? Math.Round(q, 2) : 0;
}
