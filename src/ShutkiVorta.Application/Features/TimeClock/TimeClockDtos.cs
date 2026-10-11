using System.Globalization;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Domain.TimeClock;

namespace ShutkiVorta.Application.Features.TimeClock;

/// <summary>What a shift is marked with in lists, on its page and in the export.</summary>
public enum ShiftFlag
{
    Open,
    NeedsReview,
    MissedClockOut,
    SelfCorrected,
    ClosedBySystem,
    LongShift,
    NoLocation,
    ApproximateLocation,
    FarFromKitchen,
    Edited,
    ManualEntry,
    Voided,
}

/// <summary>A clock-in or clock-out as the device reported it.</summary>
public sealed record PunchDto
{
    /// <summary>Null for a clock-out nobody punched (still open, or closed by a person or the system).</summary>
    public LocationStatus? Status { get; init; }

    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? AccuracyMeters { get; init; }

    /// <summary>Distance to the kitchen when the punch happened.</summary>
    public double? DistanceMeters { get; init; }

    public string? Note { get; init; }

    public bool HasCoordinates => Status == LocationStatus.Captured && Latitude is not null && Longitude is not null;
    public bool IsApproximate => PunchLocation.IsApproximateFix(Status, AccuracyMeters);

    /// <summary>"32.776700,-96.797000".</summary>
    public string? Coordinates => HasCoordinates
        ? string.Create(CultureInfo.InvariantCulture, $"{Latitude:0.000000},{Longitude:0.000000}")
        : null;

    /// <summary>Opens Google Maps at the reported location (the Maps app on phones).</summary>
    public string? MapUrl => Coordinates is { } query ? $"https://www.google.com/maps/search/?api=1&query={query}" : null;
}

public sealed record ShiftDto
{
    public int Id { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public DateTime ClockInUtc { get; init; }
    public DateTime ClockInLocal { get; init; }
    public DateTime? ClockOutUtc { get; init; }
    public DateTime? ClockOutLocal { get; init; }
    public PunchDto ClockIn { get; init; } = new();
    public PunchDto ClockOut { get; init; } = new();
    public ClockOutSource? ClockOutSource { get; init; }
    public bool IsManualEntry { get; init; }
    public bool NeedsReview { get; init; }
    public DateTime? OriginalClockInLocal { get; init; }
    public DateTime? OriginalClockOutLocal { get; init; }
    public string? EditedBy { get; init; }
    public DateTime? EditedAtLocal { get; init; }
    public string? EditReason { get; init; }
    public string? VoidedBy { get; init; }
    public DateTime? VoidedAtLocal { get; init; }
    public string? VoidReason { get; init; }

    /// <summary>Optimistic concurrency token, posted back with every change.</summary>
    public int Version { get; init; }

    /// <summary>The shift's length (so far, while it is open).</summary>
    public TimeSpan Duration { get; init; }

    public bool CountsTowardsHours { get; init; }
    public IReadOnlyList<ShiftFlag> Flags { get; init; } = [];

    /// <summary>The business day the shift belongs to: the day it started.</summary>
    public DateOnly Date => DateOnly.FromDateTime(ClockInLocal);

    public bool IsOpen => ClockOutUtc is null;
    public bool IsVoided => VoidedAtLocal is not null;
    public bool IsMissedClockOut => ClockOutSource == Domain.TimeClock.ClockOutSource.Missed;
    public bool WasCorrected => OriginalClockInLocal is not null;

    public bool Has(ShiftFlag flag) => Flags.Contains(flag);
}

/// <summary>An edit-history line for a shift (from the staff audit log).</summary>
public sealed record ShiftHistoryEntryDto(string Action, string? Details, string ChangedBy, DateTime ChangedAtLocal);

/// <summary>A shift's page. Only the person themselves and people who may see the team's shifts get one.</summary>
public sealed record ShiftDetailsDto
{
    public required ShiftDto Shift { get; init; }
    public string? Email { get; init; }
    public bool IsOwnShift { get; init; }

    /// <summary>The viewer may correct, close, void and review this shift.</summary>
    public bool CanManage { get; init; }

    /// <summary>Why someone who manages shifts can't change this one (their own), or null.</summary>
    public string? ManageRefusal { get; init; }

    public bool CanSeeTeam { get; init; }
    public bool KitchenLocationSet { get; init; }
    public int NearKitchenRadiusMeters { get; init; }
    public IReadOnlyList<ShiftHistoryEntryDto> History { get; init; } = [];
}

/// <summary>The shifts of one business day (by clock-in), with the hours that count.</summary>
public sealed record ShiftDayDto(DateOnly Date, IReadOnlyList<ShiftDto> Shifts)
{
    public TimeSpan Total => Shifts.Where(s => s.CountsTowardsHours).Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);
}

/// <param name="Total">Hours that count inside the week (shifts crossing its start or end are cut at the boundary).</param>
/// <param name="InProgress">Time on the clock so far in an open shift (not part of the total yet).</param>
public sealed record WorkweekShiftsDto(DateOnly Start, IReadOnlyList<ShiftDayDto> Days, TimeSpan Total, TimeSpan InProgress)
{
    public DateOnly End => Start.AddDays(6);
    public int ShiftCount => Days.Sum(d => d.Shifts.Count);
}

/// <summary>The signed-in person's time clock: their open shift and their shifts this week and last week.</summary>
public sealed record MyTimeClockDto
{
    public ShiftDto? OpenShift { get; init; }

    /// <summary>The open shift is from an earlier day or over 16 hours old: ask when they left instead of clocking out now.</summary>
    public bool OpenShiftIsStale { get; init; }

    /// <summary>The range for "When did you leave?" (business-local).</summary>
    public DateTime? LeftAtEarliestLocal { get; init; }

    public DateTime? LeftAtLatestLocal { get; init; }

    public bool RequireLocation { get; init; }
    public bool KitchenLocationSet { get; init; }
    public DateTime NowLocal { get; init; }
    public required WorkweekShiftsDto ThisWeek { get; init; }
    public required WorkweekShiftsDto LastWeek { get; init; }
}

public sealed record TimeClockPersonDto(string UserId, string Name, string Email, bool IsFormerStaff);

public sealed record OnTheClockDto(ShiftDto Shift, bool IsStale);

/// <summary>The top of the Team page: who is on the clock, what needs a look, and the people to filter by.</summary>
public sealed record TeamTimeClockDto
{
    public IReadOnlyList<OnTheClockDto> OnTheClock { get; init; } = [];
    public int NeedsReviewCount { get; init; }
    public int EditedThisWeekCount { get; init; }
    public bool KitchenLocationSet { get; init; }
    public int NearKitchenRadiusMeters { get; init; }
    public DayOfWeek WorkweekStartsOn { get; init; }
    public DateTime NowLocal { get; init; }
    public DateOnly Today { get; init; }
    public DateOnly ThisWeekStart { get; init; }

    /// <summary>Staff and former staff, for the person filter and "Add shift".</summary>
    public IReadOnlyList<TimeClockPersonDto> People { get; init; } = [];
}

/// <param name="Total">Hours that count inside the dates (null when not filtering by dates, or when showing only some shifts).</param>
public sealed record ShiftListDto(PagedResult<ShiftDto> Shifts, TimeSpan? Total, TimeSpan? InProgress);

/// <summary>One person's hours in one workweek.</summary>
public sealed record PersonWeekHoursDto(string UserId, string Name, string? Email, TimeSpan Total, TimeSpan InProgress, int ShiftCount, int FlaggedCount)
{
    public static readonly TimeSpan OvertimeAfter = TimeSpan.FromHours(40);

    public bool IsOvertime => Total > OvertimeAfter;
    public TimeSpan Overtime => IsOvertime ? Total - OvertimeAfter : TimeSpan.Zero;
}

public sealed record WorkweekHoursDto(DateOnly Start, IReadOnlyList<PersonWeekHoursDto> People)
{
    public DateOnly End => Start.AddDays(6);
}

/// <summary>Hours per person per workweek (newest week first).</summary>
public sealed record WeeklyHoursDto(DayOfWeek WorkweekStartsOn, DateOnly From, DateOnly To, IReadOnlyList<WorkweekHoursDto> Weeks);

/// <summary>A CSV download.</summary>
public sealed record TimeClockCsvFile(string FileName, byte[] Content)
{
    public const string ContentType = "text/csv; charset=utf-8";
}

internal static class TimeClockMapping
{
    public static ShiftDto ToDto(this TimeClockShift shift, IDateTimeProvider clock, TimeClockOptions options)
    {
        var now = clock.UtcNow;
        var clockIn = new PunchDto
        {
            Status = shift.ClockInLocationStatus,
            Latitude = shift.ClockInLatitude,
            Longitude = shift.ClockInLongitude,
            AccuracyMeters = shift.ClockInAccuracyMeters,
            DistanceMeters = shift.ClockInDistanceMeters,
            Note = shift.ClockInNote,
        };
        var clockOut = new PunchDto
        {
            Status = shift.ClockOutLocationStatus,
            Latitude = shift.ClockOutLatitude,
            Longitude = shift.ClockOutLongitude,
            AccuracyMeters = shift.ClockOutAccuracyMeters,
            DistanceMeters = shift.ClockOutDistanceMeters,
            Note = shift.ClockOutNote,
        };

        return new ShiftDto
        {
            Id = shift.Id,
            UserId = shift.UserId,
            EmployeeName = shift.EmployeeName,
            ClockInUtc = Utc(shift.ClockInUtc),
            ClockInLocal = clock.ToBusinessTime(shift.ClockInUtc),
            ClockOutUtc = shift.ClockOutUtc is { } outUtc ? Utc(outUtc) : null,
            ClockOutLocal = Local(clock, shift.ClockOutUtc),
            ClockIn = clockIn,
            ClockOut = clockOut,
            ClockOutSource = shift.ClockOutSource,
            IsManualEntry = shift.IsManualEntry,
            NeedsReview = shift.NeedsReview,
            OriginalClockInLocal = Local(clock, shift.OriginalClockInUtc),
            OriginalClockOutLocal = Local(clock, shift.OriginalClockOutUtc),
            EditedBy = shift.EditedBy,
            EditedAtLocal = Local(clock, shift.EditedAtUtc),
            EditReason = shift.EditReason,
            VoidedBy = shift.VoidedBy,
            VoidedAtLocal = Local(clock, shift.VoidedAtUtc),
            VoidReason = shift.VoidReason,
            Version = shift.Version,
            Duration = shift.Duration(now),
            CountsTowardsHours = shift.CountsTowardsHours,
            Flags = Flags(shift, clockIn, clockOut, options, now),
        };
    }

    private static List<ShiftFlag> Flags(TimeClockShift shift, PunchDto clockIn, PunchDto clockOut, TimeClockOptions options, DateTime now)
    {
        var flags = new List<ShiftFlag>();
        void AddIf(bool condition, ShiftFlag flag)
        {
            if (condition)
            {
                flags.Add(flag);
            }
        }

        var punchedOut = shift.ClockOutSource == ClockOutSource.Self;
        AddIf(shift.IsVoided, ShiftFlag.Voided);
        AddIf(shift.IsOpen, ShiftFlag.Open);
        AddIf(shift.NeedsReview, ShiftFlag.NeedsReview);
        AddIf(shift.IsMissedClockOut, ShiftFlag.MissedClockOut);
        AddIf(shift.ClockOutSource == ClockOutSource.SelfCorrected, ShiftFlag.SelfCorrected);
        AddIf(shift.ClockOutSource == ClockOutSource.System, ShiftFlag.ClosedBySystem);
        AddIf(!shift.IsMissedClockOut && shift.Duration(now) > ShiftRules.LongShift, ShiftFlag.LongShift);
        AddIf(!shift.IsManualEntry && (!clockIn.HasCoordinates || (punchedOut && !clockOut.HasCoordinates)), ShiftFlag.NoLocation);
        AddIf(clockIn.IsApproximate || (punchedOut && clockOut.IsApproximate), ShiftFlag.ApproximateLocation);
        AddIf(options.HasKitchenLocation && (clockIn.DistanceMeters > options.NearKitchenRadiusMeters || clockOut.DistanceMeters > options.NearKitchenRadiusMeters),
            ShiftFlag.FarFromKitchen);
        AddIf(shift.IsManualEntry, ShiftFlag.ManualEntry);
        AddIf(!shift.IsManualEntry && shift.EditedBy is not null, ShiftFlag.Edited);
        return flags;
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? Local(IDateTimeProvider clock, DateTime? utc) => utc is { } value ? clock.ToBusinessTime(value) : null;
}
