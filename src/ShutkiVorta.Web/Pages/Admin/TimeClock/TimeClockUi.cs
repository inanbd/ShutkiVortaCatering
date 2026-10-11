using System.Globalization;
using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Features.TimeClock;

namespace ShutkiVorta.Web.Pages.Admin.TimeClock;

/// <summary>Display helpers for the time clock pages.</summary>
public static class TimeClockUi
{
    public const string TimeClockPath = "/admin/time-clock";
    public const string TeamPath = "/admin/time-clock/team";

    public static string ShiftPath(int id) => $"/admin/time-clock/shifts/{id}";

    /// <summary>What needs a manager's attention stands out; plain facts stay quiet.</summary>
    public static string FlagCss(ShiftFlag flag) => flag switch
    {
        ShiftFlag.NeedsReview or ShiftFlag.MissedClockOut or ShiftFlag.LongShift => "pill-bad",
        ShiftFlag.NoLocation or ShiftFlag.ApproximateLocation or ShiftFlag.FarFromKitchen or ShiftFlag.SelfCorrected or ShiftFlag.ClosedBySystem => "pill-warn",
        ShiftFlag.Open => "pill-on",
        _ => "pill-off",
    };

    /// <summary>"7.75 h (7:45)".</summary>
    public static string Hours(TimeSpan duration) => $"{Format.DecimalHours(duration)} h ({TimeClockText.HoursMinutes(duration)})";

    /// <summary>"9:02 AM – 5:10 PM" (naming the day when it ran past midnight), "9:02 AM – now", or "9:02 AM – ?" for a missed clock-out.</summary>
    public static string Span(ShiftDto shift)
    {
        var start = Format.Time(shift.ClockInLocal);
        if (shift.IsMissedClockOut)
        {
            return $"{start} – ?";
        }

        if (shift.ClockOutLocal is not { } end)
        {
            return $"{start} – now";
        }

        return DateOnly.FromDateTime(end) == shift.Date
            ? $"{start} – {Format.Time(end)}"
            : $"{start} – {end.ToString("ddd h:mm tt", Format.Culture)}";
    }

    /// <summary>The value of a datetime-local input.</summary>
    public static string? LocalInput(DateTime? local) => local?.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
}
