using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Domain.Orders;

namespace ShutkiVorta.Web.Services;

/// <summary>Small presentation helpers used by Razor views.</summary>
public static class Ui
{
    private static readonly char[] BengaliDigits = ['০', '১', '২', '৩', '৪', '৫', '৬', '৭', '৮', '৯'];

    public static string Money(decimal amount) => Format.Currency(amount);

    public static string Qty(decimal quantity, string unit) => Format.Quantity(quantity, unit);

    public static string Number(decimal value) => value.ToString("0.##", Format.Culture);

    public static string BengaliNumber(int value) => new(value.ToString(Format.Culture).Select(c => char.IsDigit(c) ? BengaliDigits[c - '0'] : c).ToArray());

    public static string StatusCss(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "status-pending",
        OrderStatus.Confirmed => "status-confirmed",
        OrderStatus.Preparing => "status-preparing",
        OrderStatus.ReadyForPickup or OrderStatus.OutForDelivery => "status-ready",
        OrderStatus.Completed => "status-completed",
        OrderStatus.Cancelled => "status-cancelled",
        _ => string.Empty,
    };

    public static string SpiceLabel(int level) => level switch
    {
        0 => "Not spicy",
        1 => "Mild",
        2 => "Medium",
        3 => "Spicy",
        4 => "Hot",
        _ => "Very hot",
    };

    public static IEnumerable<string> Paragraphs(string? text) =>
        (text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => string.Join(' ', p.Split('\n', StringSplitOptions.TrimEntries)));

    /// <summary>Quantity choices for quick-add selects: min, min+step, ... up to max.</summary>
    public static IEnumerable<decimal> QuantityOptions(decimal min, decimal step, decimal max = 10m)
    {
        if (step <= 0)
        {
            step = 0.5m;
        }

        for (var q = min; q <= max; q += step)
        {
            yield return q;
        }
    }
}
