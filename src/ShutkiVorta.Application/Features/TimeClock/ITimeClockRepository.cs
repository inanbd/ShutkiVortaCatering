using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Domain.TimeClock;

namespace ShutkiVorta.Application.Features.TimeClock;

/// <summary>
/// Reads and changes shifts. Every change is one guarded statement, so a double tap, the person and a manager, or two managers
/// can never silently overwrite each other: the caller learns that nothing changed and says why.
/// (Other features only read "who is on the clock" through <see cref="ITimeClockStatusReader"/>.)
/// </summary>
public interface ITimeClockRepository
{
    Task<TimeClockShift?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>The person's open shift (there is at most one).</summary>
    Task<TimeClockShift?> GetOpenShiftAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>The person's most recently closed shift, voided ones excluded.</summary>
    Task<TimeClockShift?> GetLastClosedShiftAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new open shift and assigns its id. False when the person already has an open shift: the unique index
    /// refuses the second one even when two taps race.
    /// </summary>
    Task<bool> TryAddOpenShiftAsync(TimeClockShift shift, CancellationToken cancellationToken = default);

    /// <summary>Closes the person's open shift (only while it is still open); false when it was already closed.</summary>
    Task<bool> ClockOutAsync(ClockOutPunch punch, CancellationToken cancellationToken = default);

    /// <summary>Closes the person's own forgotten shift (only while it is still open), flagged for review; false when it was already closed.</summary>
    Task<bool> CloseForgottenShiftAsync(ForgottenClockOut fix, CancellationToken cancellationToken = default);

    /// <summary>
    /// A manager's new times, in one UPDATE that also checks the version and that no other shift of the person overlaps.
    /// The first correction keeps the punched times as the original ones; the edited shift then needs a review.
    /// </summary>
    Task<ShiftChangeOutcome> ChangeTimesAsync(ShiftTimesChange change, CancellationToken cancellationToken = default);

    /// <summary>Inserts a shift a manager entered (and assigns its id) unless it overlaps another shift of the person.</summary>
    Task<ShiftChangeOutcome> AddClosedShiftAsync(TimeClockShift shift, CancellationToken cancellationToken = default);

    /// <summary>Voids a closed shift at this version: it is kept and shown, but no longer counts.</summary>
    Task<bool> VoidAsync(int id, int expectedVersion, string voidedBy, string reason, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Clears the review flag of the shift at this version.</summary>
    Task<bool> MarkReviewedAsync(int id, int expectedVersion, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// One of the person's other shifts (not voided) that overlaps [<paramref name="clockInUtc"/>, <paramref name="clockOutUtc"/>);
    /// a null clock-out means "still open".
    /// </summary>
    Task<TimeClockShift?> FindOverlapAsync(string userId, DateTime clockInUtc, DateTime? clockOutUtc, int? exceptShiftId, CancellationToken cancellationToken = default);

    /// <summary>Shifts that overlap [<paramref name="startUtc"/>, <paramref name="endUtc"/>), for everyone or one person, by clock-in time.</summary>
    Task<IReadOnlyList<TimeClockShift>> ListOverlappingAsync(DateTime startUtc, DateTime endUtc, string? userId, CancellationToken cancellationToken = default);

    /// <summary>Shifts whose clock-in falls in the search's range (they belong to that day), newest first, one page.</summary>
    Task<PagedResult<TimeClockShift>> SearchAsync(ShiftSearch search, CancellationToken cancellationToken = default);

    /// <summary>Everyone on the clock now, longest first.</summary>
    Task<IReadOnlyList<TimeClockShift>> ListOpenShiftsAsync(CancellationToken cancellationToken = default);

    Task<int> CountNeedingReviewAsync(CancellationToken cancellationToken = default);

    /// <summary>Shifts changed after the punch (corrected, entered later or added by hand) since <paramref name="sinceUtc"/>.</summary>
    Task<int> CountEditedSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// The newest of the person's staff audit entries whose details start with <paramref name="detailsPrefix"/> (a shift's
    /// "Shift #12 ("): one shift's history, however many other entries the person has.
    /// </summary>
    Task<IReadOnlyList<StaffAuditEntryDto>> ListAuditEntriesAsync(string userId, string detailsPrefix, int take, CancellationToken cancellationToken = default);
}

/// <param name="NeedsReview">Flag the shift (the punch is doubtful or the shift is very long); never clears an earlier flag.</param>
public sealed record ClockOutPunch(int ShiftId, string UserId, DateTime ClockOutUtc, PunchLocation Location, double? DistanceMeters, string? Note, bool NeedsReview);

/// <param name="ClockOutUtc">When the person says they left; ignored for <see cref="ClockOutSource.Missed"/> (the clock-in time is used).</param>
public sealed record ForgottenClockOut(int ShiftId, string UserId, DateTime ClockOutUtc, ClockOutSource Source, string EditedBy, string Reason, DateTime NowUtc);

/// <param name="ClockOutUtc">Null keeps an open shift open.</param>
/// <param name="ClockOutSource">How the shift is closed from now on (unchanged when the clock-out time is).</param>
public sealed record ShiftTimesChange(
    int ShiftId, string UserId, int ExpectedVersion, DateTime ClockInUtc, DateTime? ClockOutUtc, ClockOutSource? ClockOutSource,
    string EditedBy, string Reason, DateTime NowUtc);

public enum ShiftChangeOutcome
{
    Saved,

    /// <summary>The shift was changed (or voided, or deleted) by someone else since it was loaded.</summary>
    Changed,

    /// <summary>The times overlap another shift of the same person.</summary>
    Overlaps,
}

/// <summary>Which shifts the Team list shows.</summary>
public enum ShiftFilter
{
    All,
    NeedsReview,
    Edited,
    Open,
    Voided,
}

public sealed record ShiftSearch
{
    public string? UserId { get; init; }

    /// <summary>Clock-in on or after (UTC).</summary>
    public DateTime? FromUtc { get; init; }

    /// <summary>Clock-in before (UTC).</summary>
    public DateTime? ToUtc { get; init; }

    public ShiftFilter Filter { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
