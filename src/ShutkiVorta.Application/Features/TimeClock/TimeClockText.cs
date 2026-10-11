using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Domain.TimeClock;

namespace ShutkiVorta.Application.Features.TimeClock;

/// <summary>Words and numbers for the time clock, shared by the pages and the CSV exports.</summary>
public static class TimeClockText
{
    private const double MetersPerMile = 1609.344;

    public static string Location(LocationStatus? status) => status switch
    {
        LocationStatus.Captured => "Location recorded",
        LocationStatus.PermissionDenied => "Location blocked on the device",
        LocationStatus.Unavailable => "Device couldn't find its location",
        LocationStatus.Timeout => "Location took too long",
        LocationStatus.NotSupported => "Browser can't share location",
        LocationStatus.NotProvided => "No location sent",
        LocationStatus.InsecureConnection => "Location needs a secure (https) connection",
        LocationStatus.ManualEntry => "Entered by hand",
        _ => "No punch",
    };

    /// <summary>How a shift was closed, e.g. "Clocked out" or "Entered later by the employee".</summary>
    public static string ClockOutSource(ClockOutSource? source) => source switch
    {
        Domain.TimeClock.ClockOutSource.Self => "Clocked out",
        Domain.TimeClock.ClockOutSource.SelfCorrected => "Entered later by the employee",
        Domain.TimeClock.ClockOutSource.Manager => "Set by a manager",
        Domain.TimeClock.ClockOutSource.System => "Closed by the system",
        Domain.TimeClock.ClockOutSource.Missed => "Missed: time unknown",
        _ => "Still on the clock",
    };

    public static string Flag(ShiftFlag flag) => flag switch
    {
        ShiftFlag.Open => "On the clock",
        ShiftFlag.NeedsReview => "Needs review",
        ShiftFlag.MissedClockOut => "Missed clock-out",
        ShiftFlag.SelfCorrected => "Clock-out entered later",
        ShiftFlag.ClosedBySystem => "Closed by the system",
        ShiftFlag.LongShift => $"Over {ShiftRules.LongShift.TotalHours:0} h",
        ShiftFlag.NoLocation => "No location",
        ShiftFlag.ApproximateLocation => "Approximate location",
        ShiftFlag.FarFromKitchen => "Away from the kitchen",
        ShiftFlag.Edited => "Edited",
        ShiftFlag.ManualEntry => "Added by hand",
        ShiftFlag.Voided => "Voided",
        _ => flag.ToString(),
    };

    /// <summary>"120 m" up to a kilometre, then "2.4 km (1.5 mi)".</summary>
    public static string Distance(double meters) => meters < 1000
        ? $"{meters.ToString("0", Format.Culture)} m"
        : $"{(meters / 1000).ToString("0.0", Format.Culture)} km ({(meters / MetersPerMile).ToString("0.0", Format.Culture)} mi)";

    /// <summary>Hours and minutes like a clock face, e.g. "37:30" (whole minutes; negative counts as zero).</summary>
    public static string HoursMinutes(TimeSpan duration)
    {
        var minutes = Math.Max(0, (long)Math.Floor(duration.TotalMinutes));
        return $"{minutes / 60}:{minutes % 60:00}";
    }

    /// <summary>"9:02 AM" today, "Mon at 9:02 AM" this past week, otherwise "Mon, Oct 5 at 9:02 AM".</summary>
    public static string When(DateTime local, DateTime nowLocal)
    {
        var days = DateOnly.FromDateTime(nowLocal).DayNumber - DateOnly.FromDateTime(local).DayNumber;
        return days switch
        {
            0 => Format.Time(local),
            > 0 and < 7 => local.ToString("ddd 'at' h:mm tt", Format.Culture),
            _ => local.ToString("ddd, MMM d 'at' h:mm tt", Format.Culture),
        };
    }

    /// <summary>"Mon, Oct 5".</summary>
    public static string Day(DateOnly date) => date.ToString("ddd, MMM d", Format.Culture);

    /// <summary>"Mon, Oct 5, 9:00 AM – 5:00 PM" (the end day is named when it differs), or "… from 9:00 AM (still open)".</summary>
    public static string Times(DateTime inLocal, DateTime? outLocal)
    {
        var start = $"{Day(DateOnly.FromDateTime(inLocal))}, {Format.Time(inLocal)}";
        if (outLocal is not { } end)
        {
            return $"{start} (still open)";
        }

        return DateOnly.FromDateTime(end) == DateOnly.FromDateTime(inLocal)
            ? $"{start} – {Format.Time(end)}"
            : $"{start} – {Day(DateOnly.FromDateTime(end))}, {Format.Time(end)}";
    }
}
