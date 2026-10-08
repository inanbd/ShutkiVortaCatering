using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Domain.Orders;

namespace ShutkiVorta.Application.Features.Orders;

public static class OrderingOptionsExtensions
{
    public static OrderPricingPolicy ToPricingPolicy(this OrderingOptions options) => new(
        options.DeliveryFee,
        options.FreeDeliveryThreshold,
        options.TaxRate,
        options.MinimumDeliverySubtotal,
        options.TaxDeliveryFee);

    public static bool DeliversTo(this OrderingOptions options, string? postalCode)
    {
        if (options.DeliveryZipPrefixes.Count == 0)
        {
            return true;
        }

        var zip = postalCode?.Trim() ?? string.Empty;
        return options.DeliveryZipPrefixes.Any(prefix => zip.StartsWith(prefix.Trim(), StringComparison.Ordinal));
    }
}
