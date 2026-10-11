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

    /// <summary>Time worked, e.g. "7 h 45 min", "8 h" or "45 min". Seconds are dropped; negative durations count as zero.</summary>
    public static string Hours(TimeSpan duration)
    {
        var minutes = WholeMinutes(duration);
        var (hours, rest) = (minutes / 60, minutes % 60);
        return hours == 0 ? $"{rest} min"
            : rest == 0 ? $"{hours} h"
            : $"{hours} h {rest} min";
    }

    /// <summary>Time worked in decimal hours for payroll, e.g. "7.75" (whole minutes, two decimals).</summary>
    public static string DecimalHours(TimeSpan duration) =>
        Math.Round(WholeMinutes(duration) / 60m, 2, MidpointRounding.AwayFromZero).ToString("0.00", Culture);

    private static long WholeMinutes(TimeSpan duration) => Math.Max(0, (long)Math.Floor(duration.TotalMinutes));
}
