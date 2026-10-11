using ShutkiVorta.Domain.Common;

namespace ShutkiVorta.Domain.TimeClock;

/// <summary>
/// Converting the business's wall-clock times to UTC around daylight-saving changes. Shift times are stored in UTC; people
/// type and read them in the business time zone.
/// </summary>
public static class BusinessTime
{
    public const string SkippedTimeMessage = "That time doesn't exist (clocks moved forward).";

    /// <summary>
    /// A time a person typed (business-local) as UTC. A time skipped when the clocks moved forward is refused; a time that
    /// happened twice when they moved back means the first one (daylight time).
    /// </summary>
    public static DateTime FromLocalInput(DateTime local, TimeZoneInfo zone)
    {
        var wallClock = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wallClock))
        {
            throw new DomainException(SkippedTimeMessage);
        }

        return ToUtc(wallClock, zone);
    }

    /// <summary>
    /// When a business-local day starts, in UTC. Never throws: if midnight was skipped the day starts when the clocks jumped,
    /// and if it happened twice it starts at the first one.
    /// </summary>
    public static DateTime StartOfDayUtc(DateOnly date, TimeZoneInfo zone)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(midnight))
        {
            // The offset in force before the jump turns the skipped midnight into the moment of the jump.
            var before = zone.GetUtcOffset(midnight.AddDays(-1));
            return DateTime.SpecifyKind(midnight - before, DateTimeKind.Utc);
        }

        return ToUtc(midnight, zone);
    }

    /// <summary>The business-local date of a UTC instant.</summary>
    public static DateOnly DateOf(DateTime utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));

    /// <summary>The first day of the workweek that contains <paramref name="date"/>.</summary>
    public static DateOnly WeekStart(DateOnly date, DayOfWeek firstDay) =>
        date.AddDays(-(((int)date.DayOfWeek - (int)firstDay + 7) % 7));

    private static DateTime ToUtc(DateTime wallClock, TimeZoneInfo zone)
    {
        if (zone.IsAmbiguousTime(wallClock))
        {
            var offset = zone.GetAmbiguousTimeOffsets(wallClock).Max();
            return DateTime.SpecifyKind(wallClock - offset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(wallClock, zone);
    }
}
