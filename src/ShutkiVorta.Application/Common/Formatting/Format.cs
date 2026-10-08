using System.Globalization;

namespace ShutkiVorta.Application.Common.Formatting;

/// <summary>US-English formatting helpers so output is identical regardless of server culture.</summary>
public static class Format
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    public static string Currency(decimal amount) => amount.ToString("C2", Culture);

    public static string Quantity(decimal quantity, string unit) => $"{quantity.ToString("0.##", Culture)} {unit}";

    public static string Date(DateTime value) => value.ToString("dddd, MMMM d, yyyy", Culture);

    public static string ShortDate(DateTime value) => value.ToString("MMM d, yyyy", Culture);

    public static string Time(DateTime value) => value.ToString("h:mm tt", Culture);

    public static string Time(TimeOnly value) => value.ToString("h:mm tt", Culture);

    public static string DateTime(DateTime value) => value.ToString("ddd, MMM d, yyyy 'at' h:mm tt", Culture);
}
