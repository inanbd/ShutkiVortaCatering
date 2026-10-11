using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Domain.Common;
using ShutkiVorta.Domain.TimeClock;
using ValidationException = ShutkiVorta.Application.Common.Exceptions.ValidationException;

namespace ShutkiVorta.Application.Features.TimeClock;

/// <summary>
/// What the browser reported when the page asked for the device's location: the page's script sets the status (without
/// scripts it stays <see cref="LocationStatus.NotProvided"/>). Coordinates only count with <see cref="LocationStatus.Captured"/>.
/// </summary>
public sealed record PunchLocationInput(LocationStatus Status, double? Latitude = null, double? Longitude = null, double? AccuracyMeters = null)
{
    public static PunchLocationInput NotProvided { get; } = new(LocationStatus.NotProvided);
}

/// <summary>Clocks the signed-in staff member in now (server time). There is no user parameter: it is always the caller.</summary>
public sealed record ClockInCommand(PunchLocationInput Location, string? Note = null) : IRequest<ClockResult>, IRequireStaff;

/// <summary>Clocks the signed-in staff member out of their open shift now (server time).</summary>
public sealed record ClockOutCommand(PunchLocationInput Location, string? Note = null) : IRequest<ClockResult>, IRequireStaff;

/// <summary>
/// Closes the caller's own stale shift (one from an earlier day or over 16 hours old) with the time they say they left
/// (business-local), or, when <paramref name="LeftAtLocal"/> is null, as "I don't remember" (no hours until a manager fixes it).
/// Flagged for review either way. Acts only on the caller's open shift.
/// </summary>
/// <param name="FinishingNow">
/// "I'm finishing now" on a shift that ran past midnight: it ends now (server time), like a clock-out; <paramref name="LeftAtLocal"/> is ignored.
/// </param>
public sealed record FixMyForgottenClockOutCommand(DateTime? LeftAtLocal, bool FinishingNow = false) : IRequest<ClockResult>, IRequireStaff;

/// <param name="ClockedIn">True after a clock-in; false after a clock-out or a fixed forgotten shift.</param>
/// <param name="AtLocal">When it happened (business-local).</param>
/// <param name="Repeated">The same tap arrived twice: this is the first tap's result, nothing new was recorded.</param>
/// <param name="ShiftLength">The length of the shift that ended (null after a clock-in or for "I don't remember").</param>
public sealed record ClockResult(
    bool ClockedIn, DateTime AtLocal, bool Repeated, LocationStatus? LocationStatus, bool IsApproximate, double? DistanceMeters,
    bool FlaggedForReview, TimeSpan? ShiftLength);

public sealed class PunchLocationInputValidator : AbstractValidator<PunchLocationInput>
{
    private const string Unreadable = "The location sent by your device could not be read. Please try again, or tap Skip.";

    public PunchLocationInputValidator()
    {
        RuleFor(x => x.Status).IsInEnum().NotEqual(LocationStatus.ManualEntry).WithMessage("Unknown location status.");
        When(x => x.Status == LocationStatus.Captured, () =>
        {
            // InclusiveBetween also refuses NaN and infinity, which the model binder happily parses.
            RuleFor(x => x.Latitude).NotNull().InclusiveBetween(-90, 90).WithMessage(Unreadable);
            RuleFor(x => x.Longitude).NotNull().InclusiveBetween(-180, 180).WithMessage(Unreadable);
            RuleFor(x => x.AccuracyMeters).InclusiveBetween(0, PunchLocation.MaxAccuracyMeters).When(x => x.AccuracyMeters.HasValue).WithMessage(Unreadable);
        });
    }
}

public sealed class ClockInCommandValidator : AbstractValidator<ClockInCommand>
{
    public ClockInCommandValidator()
    {
        RuleFor(x => x.Location).NotNull().SetValidator(new PunchLocationInputValidator());
        RuleFor(x => x.Note).MaximumLength(TimeClockShift.MaxNoteLength);
    }
}

public sealed class ClockOutCommandValidator : AbstractValidator<ClockOutCommand>
{
    public ClockOutCommandValidator()
    {
        RuleFor(x => x.Location).NotNull().SetValidator(new PunchLocationInputValidator());
        RuleFor(x => x.Note).MaximumLength(TimeClockShift.MaxNoteLength);
    }
}

internal sealed class TimeClockCommandHandlers(
    ITimeClockRepository repository,
    IStaffDirectory directory,
    IStaffAuditLog audit,
    ICurrentStaff currentStaff,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<TimeClockOptions> options) :
    IRequestHandler<ClockInCommand, ClockResult>,
    IRequestHandler<ClockOutCommand, ClockResult>,
    IRequestHandler<FixMyForgottenClockOutCommand, ClockResult>
{
    public async Task<ClockResult> Handle(ClockInCommand request, CancellationToken cancellationToken)
    {
        var userId = await CallerAsync(cancellationToken);
        var now = TimeClockTime.Now(clock);

        var open = await repository.GetOpenShiftAsync(userId, cancellationToken);
        if (open is not null)
        {
            return AlreadyClockedIn(open, now);
        }

        var settings = options.Value;
        var location = Locate(request.Location, settings, "clock in");
        var distance = TimeClockTime.DistanceToKitchen(location, settings);
        var flagged = ShiftRules.IsDoubtfulPunch(location, distance, settings.NearKitchenRadiusMeters);
        var name = (await directory.GetAsync(userId, cancellationToken))?.FullName ?? currentUser.Name ?? currentUser.Email ?? "Staff member";

        var shift = TimeClockShift.Open(userId, name, location, distance, request.Note, flagged, now);
        if (!await repository.TryAddOpenShiftAsync(shift, cancellationToken))
        {
            // Another tap (or another device) clocked in first.
            open = await repository.GetOpenShiftAsync(userId, cancellationToken) ?? throw new ValidationException("Please try again.");
            return AlreadyClockedIn(open, now);
        }

        return new ClockResult(true, clock.ToBusinessTime(now), false, location.Status, location.IsApproximate, distance, flagged, null);
    }

    public async Task<ClockResult> Handle(ClockOutCommand request, CancellationToken cancellationToken)
    {
        var userId = await CallerAsync(cancellationToken);
        var now = TimeClockTime.Now(clock);

        var open = await repository.GetOpenShiftAsync(userId, cancellationToken);
        if (open is null)
        {
            return await AlreadyClockedOutAsync(userId, now, cancellationToken);
        }

        if (ShiftRules.IsStale(open.ClockInUtc, now, clock.BusinessTimeZone))
        {
            // A night shift (an old page still showing "Clock out" after midnight) is not a forgotten one.
            var started = TimeClockText.When(clock.ToBusinessTime(open.ClockInUtc), clock.ToBusinessTime(now));
            throw new ValidationException(ShiftRules.RanPastMidnight(open.ClockInUtc, now, clock.BusinessTimeZone)
                ? $"Your shift started {started}, before midnight, so please confirm when you finish: tap \"I'm finishing now\"."
                : $"You didn't clock out on {started}. Please tell us when you left first.");
        }

        var settings = options.Value;
        var location = Locate(request.Location, settings, "clock out");
        var distance = TimeClockTime.DistanceToKitchen(location, settings);
        var flagged = ShiftRules.IsDoubtfulPunch(location, distance, settings.NearKitchenRadiusMeters) || now - open.ClockInUtc > ShiftRules.LongShift;

        var punch = new ClockOutPunch(open.Id, userId, now, location, distance, request.Note, flagged);
        if (!await repository.ClockOutAsync(punch, cancellationToken))
        {
            return await AlreadyClockedOutAsync(userId, now, cancellationToken);
        }

        return new ClockResult(false, clock.ToBusinessTime(now), false, location.Status, location.IsApproximate, distance, flagged, now - open.ClockInUtc);
    }

    public async Task<ClockResult> Handle(FixMyForgottenClockOutCommand request, CancellationToken cancellationToken)
    {
        var userId = await CallerAsync(cancellationToken);
        var now = TimeClockTime.Now(clock);
        var zone = clock.BusinessTimeZone;

        var open = await repository.GetOpenShiftAsync(userId, cancellationToken);
        if (open is null)
        {
            return await AlreadyFixedAsync(userId, now, "You're not clocked in, so there is no shift to fix.", cancellationToken);
        }

        if (!ShiftRules.IsStale(open.ClockInUtc, now, zone))
        {
            throw new ValidationException("Your shift started today, so just clock out.");
        }

        var finishingNow = request.FinishingNow;
        var remembered = finishingNow || request.LeftAtLocal is not null;
        DateTime leftAtUtc;
        try
        {
            leftAtUtc = finishingNow ? now : request.LeftAtLocal is { } leftAt ? BusinessTime.FromLocalInput(leftAt, zone) : open.ClockInUtc;
            if (remembered)
            {
                ShiftRules.CheckForgottenClockOut(open.ClockInUtc, leftAtUtc, now);
            }
        }
        catch (DomainException ex)
        {
            throw new ValidationException(nameof(FixMyForgottenClockOutCommand.LeftAtLocal), ex.Message);
        }

        var source = remembered ? ClockOutSource.SelfCorrected : ClockOutSource.Missed;
        var by = currentUser.Email ?? open.EmployeeName;
        var reason = finishingNow ? "Shift ran past midnight; tapped \"I'm finishing now\""
            : remembered ? "Forgot to clock out; entered when they left" : "Forgot to clock out; doesn't remember when they left";
        if (!await repository.CloseForgottenShiftAsync(new ForgottenClockOut(open.Id, userId, leftAtUtc, source, by, reason, now), cancellationToken))
        {
            return await AlreadyFixedAsync(userId, now, "That shift was already closed. Reload the page to see it.", cancellationToken);
        }

        var inLocal = clock.ToBusinessTime(open.ClockInUtc);
        var times = TimeClockText.Times(inLocal, clock.ToBusinessTime(leftAtUtc));
        var details = finishingNow ? $"shift ran past midnight; finished {times} (tapped \"I'm finishing now\")."
            : remembered ? $"forgot to clock out; says they left at {times}."
            : "forgot to clock out and doesn't remember when they left (no hours count until a manager fixes it).";
        await audit.WriteAsync(new StaffAuditEntry(userId, open.EmployeeName, StaffAuditActions.ShiftSelfCorrected, ShiftAudit.Details(open.Id, inLocal, details), by), cancellationToken);

        return new ClockResult(false, clock.ToBusinessTime(leftAtUtc), false, null, false, null, true, remembered ? leftAtUtc - open.ClockInUtc : null);
    }

    /// <summary>
    /// No open shift (any more): a second tap within a couple of minutes of closing it on this screen shows the first tap's
    /// result; otherwise <paramref name="refusal"/>.
    /// </summary>
    private async Task<ClockResult> AlreadyFixedAsync(string userId, DateTime now, string refusal, CancellationToken cancellationToken)
    {
        var last = await repository.GetLastClosedShiftAsync(userId, cancellationToken);
        if (last is { ClockOutSource: ClockOutSource.SelfCorrected or ClockOutSource.Missed, ClockOutUtc: { } closedAt, EditedAtUtc: { } fixedAt }
            && now - fixedAt <= ShiftRules.RepeatTapWindow && now >= fixedAt)
        {
            var remembered = last.ClockOutSource == ClockOutSource.SelfCorrected;
            return new ClockResult(false, clock.ToBusinessTime(closedAt), true, null, false, null, true, remembered ? closedAt - last.ClockInUtc : null);
        }

        throw new ValidationException(refusal);
    }

    /// <summary>The caller's own user id (the request marker guarantees staff access).</summary>
    private async Task<string> CallerAsync(CancellationToken cancellationToken) =>
        (await currentStaff.GetAsync(cancellationToken)).UserId ?? throw new InvalidOperationException("No signed-in staff member.");

    /// <summary>A second tap within a couple of minutes shows the first tap's result; later it is a friendly error.</summary>
    private ClockResult AlreadyClockedIn(TimeClockShift open, DateTime now)
    {
        if (now - open.ClockInUtc <= ShiftRules.RepeatTapWindow && now >= open.ClockInUtc)
        {
            return new ClockResult(true, clock.ToBusinessTime(open.ClockInUtc), true, open.ClockInLocationStatus,
                PunchLocation.IsApproximateFix(open.ClockInLocationStatus, open.ClockInAccuracyMeters), open.ClockInDistanceMeters, open.NeedsReview, null);
        }

        throw new ValidationException($"You're already clocked in since {TimeClockText.When(clock.ToBusinessTime(open.ClockInUtc), clock.ToBusinessTime(now))}.");
    }

    private async Task<ClockResult> AlreadyClockedOutAsync(string userId, DateTime now, CancellationToken cancellationToken)
    {
        var last = await repository.GetLastClosedShiftAsync(userId, cancellationToken);
        if (last is { ClockOutSource: ClockOutSource.Self, ClockOutUtc: { } clockedOut } && now - clockedOut <= ShiftRules.RepeatTapWindow && now >= clockedOut)
        {
            return new ClockResult(false, clock.ToBusinessTime(clockedOut), true, last.ClockOutLocationStatus,
                PunchLocation.IsApproximateFix(last.ClockOutLocationStatus, last.ClockOutAccuracyMeters), last.ClockOutDistanceMeters, last.NeedsReview,
                clockedOut - last.ClockInUtc);
        }

        throw new ValidationException(last?.ClockOutUtc is { } ended
            ? $"You're not clocked in. Your last shift ended {TimeClockText.When(clock.ToBusinessTime(ended), clock.ToBusinessTime(now))}."
            : "You're not clocked in.");
    }

    /// <summary>The punch's location; refused (with how to fix it) when locations are required and the device sent none.</summary>
    private static PunchLocation Locate(PunchLocationInput input, TimeClockOptions settings, string action)
    {
        PunchLocation location;
        try
        {
            location = input.Status == LocationStatus.Captured
                ? PunchLocation.Captured(input.Latitude ?? double.NaN, input.Longitude ?? double.NaN, input.AccuracyMeters)
                : PunchLocation.Missing(input.Status);
        }
        catch (DomainException ex)
        {
            throw new ValidationException(ex.Message);
        }

        if (settings.RequireLocation && !location.HasCoordinates)
        {
            throw new ValidationException(nameof(ClockInCommand.Location), LocationRequired(location.Status, action));
        }

        return location;
    }

    private static string LocationRequired(LocationStatus status, string action)
    {
        var why = status switch
        {
            LocationStatus.InsecureConnection => "this page isn't using a secure (https) connection, so your device won't share it. Please tell an admin.",
            LocationStatus.PermissionDenied => "location is blocked for this site on your device. See \"How to turn location on\" below, then try again.",
            LocationStatus.Unavailable or LocationStatus.Timeout => "your device couldn't find it. Check that Location is on, move near a window or outside, and try again.",
            LocationStatus.NotSupported => "this browser can't share it. Try your phone's own browser (Safari or Chrome).",
            _ => "allow location when your device asks, then try again.",
        };
        return $"Your location is needed to {action}, but {why} If it still doesn't work, ask a manager to add your shift.";
    }
}

/// <summary>Small helpers shared by the time clock handlers.</summary>
internal static class TimeClockTime
{
    /// <summary>Now, to the whole second: punches don't need more, and both databases then store exactly the same value.</summary>
    public static DateTime Now(IDateTimeProvider clock)
    {
        var now = clock.UtcNow;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }

    /// <summary>Distance to the kitchen (to 0.1 m), or null without a location or without the kitchen's location.</summary>
    public static double? DistanceToKitchen(PunchLocation location, TimeClockOptions settings) =>
        location.HasCoordinates && settings is { KitchenLatitude: { } lat, KitchenLongitude: { } lon }
            ? Math.Round(GeoDistance.Meters(location.Latitude!.Value, location.Longitude!.Value, lat, lon), 1)
            : null;
}

/// <summary>How shift changes are described in the staff audit log ("Shift #12 (Mon, Oct 5): …"), so a shift's page can list its history.</summary>
internal static class ShiftAudit
{
    /// <summary>Not in <see cref="StaffAuditActions"/>: clearing a review flag changes no times.</summary>
    public const string ShiftReviewed = "ShiftReviewed";

    public static string Details(int shiftId, DateTime clockInLocal, string what) =>
        $"{Prefix(shiftId)}{TimeClockText.Day(DateOnly.FromDateTime(clockInLocal))}): {what}";

    public static bool IsAbout(StaffAuditEntryDto entry, int shiftId) =>
        entry.Details?.StartsWith(Prefix(shiftId), StringComparison.Ordinal) == true;

    public static string ActionName(string action) => action == ShiftReviewed ? "Marked as reviewed" : StaffAuditActions.DisplayName(action);

    /// <summary>"Shift #12 (": how every entry about the shift starts.</summary>
    public static string Prefix(int shiftId) => $"Shift #{shiftId} (";
}
