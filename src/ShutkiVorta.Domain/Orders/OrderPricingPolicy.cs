using ShutkiVorta.Domain.Common;

namespace ShutkiVorta.Domain.Orders;

/// <summary>Fees and taxes applied when pricing an order.</summary>
/// <param name="DeliveryFee">Flat delivery charge.</param>
/// <param name="FreeDeliveryThreshold">Subtotal at or above which delivery is free; <c>null</c> disables free delivery.</param>
/// <param name="TaxRate">Sales tax rate, e.g. 0.0825 for Dallas, TX.</param>
/// <param name="MinimumDeliverySubtotal">Smallest food subtotal accepted for delivery orders.</param>
/// <param name="TaxDeliveryFee">Whether the delivery fee is part of the taxable amount.</param>
public sealed record OrderPricingPolicy(
    decimal DeliveryFee,
    decimal? FreeDeliveryThreshold,
    decimal TaxRate,
    decimal MinimumDeliverySubtotal,
    bool TaxDeliveryFee = true)
{
    public OrderTotals Calculate(decimal subtotal, FulfillmentMethod fulfillment)
    {
        subtotal = Money.Round(subtotal);

        var deliveryFee = 0m;
        if (fulfillment == FulfillmentMethod.Delivery && subtotal > 0)
        {
            var isFree = FreeDeliveryThreshold is { } threshold && subtotal >= threshold;
            deliveryFee = isFree ? 0m : Money.Round(DeliveryFee);
        }

        var taxable = TaxDeliveryFee ? subtotal + deliveryFee : subtotal;
        var tax = Money.Round(taxable * TaxRate);

        return new OrderTotals(subtotal, deliveryFee, tax, subtotal + deliveryFee + tax);
    }
}

public sealed record OrderTotals(decimal Subtotal, decimal DeliveryFee, decimal Tax, decimal Total);
