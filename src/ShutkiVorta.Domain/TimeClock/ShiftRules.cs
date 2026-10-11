using ShutkiVorta.Domain.Common;

namespace ShutkiVorta.Domain.TimeClock;

/// <summary>The time clock's rules about shift lengths, forgotten clock-outs, hours in a period and doubtful punches.</summary>
public static class ShiftRules
{
    /// <summary>Longer shifts are flagged, and an open shift this old is treated as a forgotten clock-out.</summary>
    public static readonly TimeSpan LongShift = TimeSpan.FromHours(16);

    /// <summary>The longest shift a manager can enter or correct.</summary>
    public static readonly TimeSpan MaxEditedShift = TimeSpan.FromHours(24);

    /// <summary>A second tap on the same button within this time shows the first tap's result instead of an error.</summary>
    public static readonly TimeSpan RepeatTapWindow = TimeSpan.FromMinutes(2);

    /// <summary>No shift starts before this date: an earlier year is a typo (and SQL Server's datetime starts in 1753).</summary>
    public static readonly DateOnly EarliestDate = new(2000, 1, 1);

    /// <summary>
    /// An open shift the person most likely forgot to clock out of: it started on an earlier business day, or more than
    /// <see cref="LongShift"/> ago. They are asked when they left instead of being clocked out now.
    /// </summary>
    public static bool IsStale(DateTime clockInUtc, DateTime nowUtc, TimeZoneInfo zone) =>
        BusinessTime.DateOf(clockInUtc, zone) < BusinessTime.DateOf(nowUtc, zone) || nowUtc - clockInUtc > LongShift;

    /// <summary>
    /// The part of a shift inside [<paramref name="startUtc"/>, <paramref name="endUtc"/>):
    /// max(0, min(out ?? now, end) − max(in, start)). UTC arithmetic only, so daylight-saving changes count correctly.
    /// </summary>
    public static TimeSpan Clip(DateTime clockInUtc, DateTime? clockOutUtc, DateTime nowUtc, DateTime startUtc, DateTime endUtc)
    {
        var end = Min(clockOutUtc ?? nowUtc, endUtc);
        var start = Max(clockInUtc, startUtc);
        return end > start ? end - start : TimeSpan.Zero;
    }

    /// <summary>
    /// Times a manager enters for a shift: the clock-out after the clock-in, nothing in the future or before
    /// <see cref="EarliestDate"/>, at most 24 hours.
    /// </summary>
    /// <param name="clockOutUtc">Null while the shift stays open.</param>
    public static void CheckTimes(DateTime clockInUtc, DateTime? clockOutUtc, DateTime nowUtc)
    {
        if (clockInUtc > nowUtc)
        {
            throw new DomainException("A shift can't start in the future.");
        }

        if (clockInUtc < EarliestDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
        {
            throw new DomainException($"Please check the year: a shift can't start before {EarliestDate.Year}.");
        }

        if (clockOutUtc is not { } clockOut)
        {
            return;
        }

        if (clockOut <= clockInUtc)
        {
            throw new DomainException("The clock-out must be after the clock-in.");
        }

        if (clockOut > nowUtc)
        {
            throw new DomainException("A shift can't end in the future.");
        }

        if (clockOut - clockInUtc > MaxEditedShift)
        {
            throw new DomainException($"A shift can be at most {MaxEditedShift.TotalHours:0} hours long.");
        }
    }

    /// <summary>When someone who forgot to clock out says they left: after they clocked in, not in the future, at most 16 hours later.</summary>
    public static void CheckForgottenClockOut(DateTime clockInUtc, DateTime leftAtUtc, DateTime nowUtc)
    {
        if (leftAtUtc <= clockInUtc)
        {
            throw new DomainException("The time you left must be after you clocked in.");
        }

        if (leftAtUtc > nowUtc)
        {
            throw new DomainException("The time you left can't be in the future.");
        }

        if (leftAtUtc - clockInUtc > LongShift)
        {
            throw new DomainException($"That would be a shift of more than {LongShift.TotalHours:0} hours. If you really worked that long, ask a manager to enter it.");
        }
    }

    /// <summary>
    /// A punch a manager should look at: no location, only an approximate one, or farther from the kitchen than the
    /// "near the kitchen" radius (when the kitchen's location is set).
    /// </summary>
    public static bool IsDoubtfulPunch(PunchLocation location, double? distanceMeters, double nearKitchenRadiusMeters) =>
        !location.HasCoordinates || location.IsApproximate || distanceMeters > nearKitchenRadiusMeters;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
