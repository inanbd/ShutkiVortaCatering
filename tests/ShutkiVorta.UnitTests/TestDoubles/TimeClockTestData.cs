using System.Reflection;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Domain.TimeClock;

namespace ShutkiVorta.UnitTests.TestDoubles;

/// <summary>
/// A clock in a real time zone (Dallas by default), so daylight-saving changes and business days are exercised.
/// (<see cref="FakeClock"/> uses UTC as the business time zone.)
/// </summary>
internal sealed class ZonedClock(DateTime utcNow, string timeZoneId = "America/Chicago") : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    public TimeZoneInfo BusinessTimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    public DateTime BusinessNow => ToBusinessTime(UtcNow);

    public DateTime ToBusinessTime(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), BusinessTimeZone);

    public DateTime ToUtc(DateTime businessLocal) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(businessLocal, DateTimeKind.Unspecified), BusinessTimeZone);

    /// <summary>Sets the clock to a Dallas wall-clock time.</summary>
    public ZonedClock At(int year, int month, int day, int hour, int minute = 0)
    {
        UtcNow = ToUtc(new DateTime(year, month, day, hour, minute, 0));
        return this;
    }
}

/// <summary>Shifts in any state, as the repository would load them.</summary>
internal static class TestShifts
{
    public static readonly TimeZoneInfo Dallas = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    /// <summary>A Dallas wall-clock time as UTC.</summary>
    public static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified), Dallas);

    /// <summary>A shift punched on a device (open when <paramref name="clockOutUtc"/> is null).</summary>
    public static TimeClockShift Punched(
        int id, string userId, DateTime clockInUtc, DateTime? clockOutUtc = null, string name = "Rahim Uddin", PunchLocation? location = null)
    {
        var shift = TimeClockShift.Open(userId, name, location ?? PunchLocation.Captured(32.7767, -96.797, 12), null, null, false, clockInUtc);
        shift.AssignId(id);
        if (clockOutUtc is { } clockOut)
        {
            Set(shift, nameof(TimeClockShift.ClockOutUtc), clockOut);
            Set(shift, nameof(TimeClockShift.ClockOutSource), ClockOutSource.Self);
            Set(shift, nameof(TimeClockShift.ClockOutLocationStatus), LocationStatus.Captured);
        }

        return shift;
    }

    public static TimeClockShift With(this TimeClockShift shift, string property, object? value)
    {
        Set(shift, property, value);
        return shift;
    }

    private static void Set(TimeClockShift shift, string property, object? value) =>
        typeof(TimeClockShift).GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.SetValue(shift, value);
}
