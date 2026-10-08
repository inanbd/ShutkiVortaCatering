using MediatR;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Domain.Orders;

namespace ShutkiVorta.Application.Features.Orders;

public sealed record CartLineInput(int MenuItemId, decimal Quantity);

/// <summary>Prices the visitor's cart against the live menu (the cart itself only stores item ids and quantities).</summary>
public sealed record PriceCartQuery(IReadOnlyList<CartLineInput> Lines) : IRequest<CartQuoteDto>;

public sealed record CartQuoteLineDto
{
    public int MenuItemId { get; init; }
    public required string Name { get; init; }
    public string? BengaliName { get; init; }
    public required string Slug { get; init; }
    public string? ImageUrl { get; init; }
    public string? ImageAlt { get; init; }
    public required string Unit { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Quantity { get; init; }
    public decimal LineTotal { get; init; }
    public decimal MinimumQuantity { get; init; }
    public decimal QuantityStep { get; init; }
    public bool IsAvailable { get; init; }
    public string? Error { get; init; }
}

public sealed record CartQuoteDto
{
    public IReadOnlyList<CartQuoteLineDto> Lines { get; init; } = [];
    public IReadOnlyList<int> MissingItemIds { get; init; } = [];
    public required OrderTotals PickupTotals { get; init; }
    public required OrderTotals DeliveryTotals { get; init; }
    public decimal MinimumDeliverySubtotal { get; init; }
    public decimal? FreeDeliveryThreshold { get; init; }

    public bool IsEmpty => Lines.Count == 0;
    public bool HasErrors => Lines.Any(l => l.Error is not null);
    public decimal Subtotal => PickupTotals.Subtotal;
    public bool MeetsDeliveryMinimum => Subtotal >= MinimumDeliverySubtotal;
    public decimal? AmountToFreeDelivery =>
        FreeDeliveryThreshold is { } t && Subtotal < t ? t - Subtotal : null;
}

internal sealed class PriceCartQueryHandler(IMenuItemRepository menu, IOptions<OrderingOptions> options)
    : IRequestHandler<PriceCartQuery, CartQuoteDto>
{
    public async Task<CartQuoteDto> Handle(PriceCartQuery request, CancellationToken cancellationToken)
    {
        var merged = request.Lines
            .Where(l => l.Quantity > 0)
            .GroupBy(l => l.MenuItemId)
            .Select(g => new CartLineInput(g.Key, g.Sum(l => l.Quantity)))
            .ToList();

        var ids = merged.Select(l => l.MenuItemId).ToArray();
        var items = ids.Length == 0
            ? new Dictionary<int, Domain.Menu.MenuItem>()
            : (await menu.GetByIdsAsync(ids, cancellationToken)).ToDictionary(i => i.Id);

        var lines = new List<CartQuoteLineDto>();
        var missing = new List<int>();
        foreach (var input in merged)
        {
            if (!items.TryGetValue(input.MenuItemId, out var item))
            {
                missing.Add(input.MenuItemId);
                continue;
            }

            var error = !item.IsAvailable ? $"{item.Name} is currently unavailable. Please remove it." : item.ValidateQuantity(input.Quantity);
            lines.Add(new CartQuoteLineDto
            {
                MenuItemId = item.Id,
                Name = item.Name,
                BengaliName = item.BengaliName,
                Slug = item.Slug,
                ImageUrl = item.ImageUrl,
                ImageAlt = item.ImageAlt,
                Unit = item.Unit,
                UnitPrice = item.PricePerUnit,
                Quantity = input.Quantity,
                LineTotal = Domain.Common.Money.Round(item.PricePerUnit * input.Quantity),
                MinimumQuantity = item.MinimumQuantity,
                QuantityStep = item.QuantityStep,
                IsAvailable = item.IsAvailable,
                Error = error,
            });
        }

        var pricing = options.Value.ToPricingPolicy();
        var subtotal = lines.Where(l => l.IsAvailable).Sum(l => l.LineTotal);

        return new CartQuoteDto
        {
            Lines = lines,
            MissingItemIds = missing,
            PickupTotals = pricing.Calculate(subtotal, FulfillmentMethod.Pickup),
            DeliveryTotals = pricing.Calculate(subtotal, FulfillmentMethod.Delivery),
            MinimumDeliverySubtotal = options.Value.MinimumDeliverySubtotal,
            FreeDeliveryThreshold = options.Value.FreeDeliveryThreshold,
        };
    }
}
