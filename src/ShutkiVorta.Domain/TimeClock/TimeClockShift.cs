using ShutkiVorta.Domain.Common;

namespace ShutkiVorta.Domain.TimeClock;

/// <summary>
/// One shift: when a person clocked in and out (UTC), and what their device reported about its location at each punch.
/// Later changes (clocking out, corrections, voiding) are single guarded statements in the repository, so that two taps or
/// two managers can never silently overwrite each other; this class describes a stored shift and creates new ones.
/// </summary>
public sealed class TimeClockShift : Entity
{
    public const int MaxNoteLength = 500;
    public const int MaxReasonLength = 500;
    public const int MaxNameLength = 120;

    // Required by the data mapper.
    private TimeClockShift()
    {
    }

    public string UserId { get; private set; } = string.Empty;

    /// <summary>The person's name when the shift was recorded (kept if the account is renamed or removed).</summary>
    public string EmployeeName { get; private set; } = string.Empty;

    public DateTime ClockInUtc { get; private set; }
    public double? ClockInLatitude { get; private set; }
    public double? ClockInLongitude { get; private set; }
    public double? ClockInAccuracyMeters { get; private set; }

    /// <summary>Distance to the kitchen at the time of the punch; null when the kitchen location was not set or there was no location.</summary>
    public double? ClockInDistanceMeters { get; private set; }

    public LocationStatus ClockInLocationStatus { get; private set; }
    public string? ClockInNote { get; private set; }

    /// <summary>Null while the shift is open.</summary>
    public DateTime? ClockOutUtc { get; private set; }

    public double? ClockOutLatitude { get; private set; }
    public double? ClockOutLongitude { get; private set; }
    public double? ClockOutAccuracyMeters { get; private set; }
    public double? ClockOutDistanceMeters { get; private set; }

    /// <summary>Null when nobody punched out on a device (still open, or closed by a person or the system).</summary>
    public LocationStatus? ClockOutLocationStatus { get; private set; }

    public string? ClockOutNote { get; private set; }
    public ClockOutSource? ClockOutSource { get; private set; }

    /// <summary>Added by a manager for someone who could not clock in (dead phone, no signal…).</summary>
    public bool IsManualEntry { get; private set; }

    /// <summary>
    /// Flagged for a manager until someone marks it reviewed: a doubtful punch, a very long shift, a forgotten clock-out, a
    /// shift closed by the system, or one a manager corrected or added by hand.
    /// </summary>
    public bool NeedsReview { get; private set; }

    /// <summary>The punched times before the first correction (null until a manager corrects the shift).</summary>
    public DateTime? OriginalClockInUtc { get; private set; }

    public DateTime? OriginalClockOutUtc { get; private set; }

    /// <summary>Who last changed the times after the punch (manager, the person themselves or the system), when and why.</summary>
    public string? EditedBy { get; private set; }

    public DateTime? EditedAtUtc { get; private set; }
    public string? EditReason { get; private set; }

    public string? VoidedBy { get; private set; }
    public DateTime? VoidedAtUtc { get; private set; }
    public string? VoidReason { get; private set; }

    /// <summary>Optimistic concurrency token: +1 on every change.</summary>
    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public bool IsOpen => ClockOutUtc is null;
    public bool IsVoided => VoidedAtUtc is not null;
    public bool IsMissedClockOut => ClockOutSource == TimeClock.ClockOutSource.Missed;

    /// <summary>Closed shifts count; open ones only "so far", and voided ones and missed clock-outs (until fixed) not at all.</summary>
    public bool CountsTowardsHours => !IsOpen && !IsVoided && !IsMissedClockOut;

    /// <summary>The shift's length (so far, while it is open).</summary>
    public TimeSpan Duration(DateTime nowUtc) => ShiftRules.Clip(ClockInUtc, ClockOutUtc, nowUtc, DateTime.MinValue, DateTime.MaxValue);

    /// <summary>Hours that count inside [<paramref name="startUtc"/>, <paramref name="endUtc"/>), in whole minutes (<see cref="ShiftRules.CountedTime"/>).</summary>
    public TimeSpan HoursWithin(DateTime startUtc, DateTime endUtc) =>
        CountsTowardsHours ? ShiftRules.CountedTime(ShiftRules.Clip(ClockInUtc, ClockOutUtc, ClockOutUtc!.Value, startUtc, endUtc)) : TimeSpan.Zero;

    /// <summary>Time worked so far inside the period while the shift is still open (not part of the totals).</summary>
    public TimeSpan InProgressWithin(DateTime startUtc, DateTime endUtc, DateTime nowUtc) =>
        IsOpen && !IsVoided ? ShiftRules.Clip(ClockInUtc, null, nowUtc, startUtc, endUtc) : TimeSpan.Zero;

    /// <summary>A clock-in on the person's device: opens a shift now.</summary>
    /// <param name="needsReview">The punch is doubtful (see <see cref="ShiftRules.IsDoubtfulPunch"/>).</param>
    public static TimeClockShift Open(
        string userId, string employeeName, PunchLocation location, double? distanceMeters, string? note, bool needsReview, DateTime nowUtc)
    {
        if (location.Status == LocationStatus.ManualEntry)
        {
            throw new DomainException("A clock-in needs the device's location status.");
        }

        return new TimeClockShift
        {
            UserId = Guard.NotEmpty(userId, "Person", 450),
            EmployeeName = Name(employeeName),
            ClockInUtc = nowUtc,
            ClockInLatitude = location.Latitude,
            ClockInLongitude = location.Longitude,
            ClockInAccuracyMeters = location.AccuracyMeters,
            ClockInDistanceMeters = location.HasCoordinates ? distanceMeters : null,
            ClockInLocationStatus = location.Status,
            ClockInNote = Guard.Optional(note, "Note", MaxNoteLength),
            NeedsReview = needsReview,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }

    /// <summary>
    /// A shift a manager enters for someone who could not clock in. It is closed, with no device location, and needs a review
    /// like every edited shift.
    /// </summary>
    public static TimeClockShift AddByHand(
        string userId, string employeeName, DateTime clockInUtc, DateTime clockOutUtc, string addedBy, string reason, DateTime nowUtc)
    {
        ShiftRules.CheckTimes(clockInUtc, clockOutUtc, nowUtc);
        return new TimeClockShift
        {
            UserId = Guard.NotEmpty(userId, "Person", 450),
            EmployeeName = Name(employeeName),
            ClockInUtc = clockInUtc,
            ClockInLocationStatus = LocationStatus.ManualEntry,
            ClockOutUtc = clockOutUtc,
            ClockOutLocationStatus = LocationStatus.ManualEntry,
            ClockOutSource = TimeClock.ClockOutSource.Manager,
            IsManualEntry = true,
            NeedsReview = true,
            EditedBy = Guard.NotEmpty(addedBy, "Added by", 256),
            EditedAtUtc = nowUtc,
            EditReason = Guard.NotEmpty(reason, "Reason", MaxReasonLength),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }

    private static string Name(string? name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "Staff member" : name.Trim();
        return trimmed.Length <= MaxNameLength ? trimmed : trimmed[..MaxNameLength];
    }
}
