using FluentValidation;
using MediatR;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Domain.Common;
using ShutkiVorta.Domain.TimeClock;
using ValidationException = ShutkiVorta.Application.Common.Exceptions.ValidationException;

namespace ShutkiVorta.Application.Features.TimeClock;

// Managers fix OTHER people's shifts. Nobody but an Admin changes their own: "Ask an admin to fix your own shifts".
// Every change is written to the staff audit log.

/// <summary>
/// New clock-in and clock-out times (business-local) for a shift. A null clock-out keeps an open shift open; a clock-out for
/// an open shift closes it. The first correction keeps the punched times as the original ones.
/// </summary>
[RequirePermission(Permissions.TimeClockManage)]
public sealed record CorrectShiftCommand(int Id, int Version, DateTime ClockInLocal, DateTime? ClockOutLocal, string Reason) : IRequest;

/// <summary>Closes an open shift someone forgot to clock out of, at the time they left (business-local).</summary>
[RequirePermission(Permissions.TimeClockManage)]
public sealed record CloseShiftCommand(int Id, int Version, DateTime ClockOutLocal, string Reason) : IRequest;

/// <summary>Adds a shift for someone who could not clock in (dead phone, no signal…). Returns the new shift's id.</summary>
[RequirePermission(Permissions.TimeClockManage)]
public sealed record AddShiftCommand(string UserId, DateTime ClockInLocal, DateTime ClockOutLocal, string Reason) : IRequest<int>;

/// <summary>Voids a shift recorded by mistake: it is kept (shown struck through) but no longer counts.</summary>
[RequirePermission(Permissions.TimeClockManage)]
public sealed record VoidShiftCommand(int Id, int Version, string Reason) : IRequest;

/// <summary>A manager has looked at a flagged shift and it is fine as it is.</summary>
[RequirePermission(Permissions.TimeClockManage)]
public sealed record MarkShiftReviewedCommand(int Id, int Version) : IRequest;

internal static class ShiftReasonRules
{
    public static void ValidReason<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Please say why, e.g. \"forgot to clock out\".")
            .MaximumLength(TimeClockShift.MaxReasonLength);
}

public sealed class CorrectShiftCommandValidator : AbstractValidator<CorrectShiftCommand>
{
    public CorrectShiftCommandValidator() => RuleFor(x => x.Reason).ValidReason();
}

public sealed class CloseShiftCommandValidator : AbstractValidator<CloseShiftCommand>
{
    public CloseShiftCommandValidator() => RuleFor(x => x.Reason).ValidReason();
}

public sealed class AddShiftCommandValidator : AbstractValidator<AddShiftCommand>
{
    public AddShiftCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("Choose who worked the shift.");
        RuleFor(x => x.Reason).ValidReason();
    }
}

public sealed class VoidShiftCommandValidator : AbstractValidator<VoidShiftCommand>
{
    public VoidShiftCommandValidator() => RuleFor(x => x.Reason).ValidReason();
}

internal sealed class ShiftManagementHandlers(
    ITimeClockRepository repository,
    IStaffDirectory directory,
    IStaffAuditLog audit,
    ICurrentStaff currentStaff,
    ICurrentUser currentUser,
    IDateTimeProvider clock) :
    IRequestHandler<CorrectShiftCommand>,
    IRequestHandler<CloseShiftCommand>,
    IRequestHandler<AddShiftCommand, int>,
    IRequestHandler<VoidShiftCommand>,
    IRequestHandler<MarkShiftReviewedCommand>
{
    public const string OwnShiftRefusal = "Ask an admin to fix your own shifts.";
    private const string ChangedMeanwhile = "Someone changed this shift while you were looking at it. Reload the page to see the latest and try again.";

    private string By => currentUser.Email ?? "Manager";

    public async Task Handle(CorrectShiftCommand request, CancellationToken cancellationToken)
    {
        var shift = await LoadAsync(request.Id, cancellationToken);
        if (shift.IsVoided)
        {
            throw new ValidationException("This shift was voided, so its times can't be changed.");
        }

        // The form shows the saved times to the minute, in business time, without the UTC offset. A time left as shown keeps
        // the saved one exactly (its seconds, and which 1:30 it was on the night the clocks go back): only what the manager
        // changed changes, and an untouched form is recognised as such.
        var now = TimeClockTime.Now(clock);
        var clockIn = ShowsAs(shift.ClockInUtc, request.ClockInLocal)
            ? shift.ClockInUtc
            : ToUtc(request.ClockInLocal, nameof(CorrectShiftCommand.ClockInLocal));
        DateTime? clockOut = request.ClockOutLocal is not { } local ? null
            : shift.ClockOutUtc is { } savedOut && ShowsAs(savedOut, local) ? savedOut
            : ToUtc(local, nameof(CorrectShiftCommand.ClockOutLocal), after: clockIn);
        if (clockOut is null && !shift.IsOpen)
        {
            throw new ValidationException(nameof(CorrectShiftCommand.ClockOutLocal), "Enter when the shift ended.");
        }

        CheckTimes(clockIn, clockOut, now);
        if (clockIn == shift.ClockInUtc && clockOut == shift.ClockOutUtc)
        {
            throw new ValidationException("Those are the times already saved.");
        }

        // A missed clock-out's stored time is only a placeholder: once a manager has entered times, they are the real ones
        // (and count), even when the clock-out typed is that same placeholder.
        var source = clockOut == shift.ClockOutUtc && !shift.IsMissedClockOut ? shift.ClockOutSource : ClockOutSource.Manager;
        await SaveTimesAsync(new ShiftTimesChange(shift.Id, shift.UserId, request.Version, clockIn, clockOut, source, By, request.Reason.Trim(), now), cancellationToken);

        var before = TimeClockText.Times(clock.ToBusinessTime(shift.ClockInUtc), Local(shift.ClockOutUtc));
        var after = TimeClockText.Times(clock.ToBusinessTime(clockIn), Local(clockOut));
        await WriteAuditAsync(shift, StaffAuditActions.ShiftCorrected, $"{before} → {after}. Reason: {request.Reason.Trim()}", cancellationToken);
    }

    public async Task Handle(CloseShiftCommand request, CancellationToken cancellationToken)
    {
        var shift = await LoadAsync(request.Id, cancellationToken);
        if (!shift.IsOpen)
        {
            throw new ValidationException("This shift is already closed. Correct its times instead.");
        }

        var now = TimeClockTime.Now(clock);
        var clockOut = ToUtc(request.ClockOutLocal, nameof(CloseShiftCommand.ClockOutLocal), after: shift.ClockInUtc);
        CheckTimes(shift.ClockInUtc, clockOut, now);

        await SaveTimesAsync(
            new ShiftTimesChange(shift.Id, shift.UserId, request.Version, shift.ClockInUtc, clockOut, ClockOutSource.Manager, By, request.Reason.Trim(), now),
            cancellationToken);

        var times = TimeClockText.Times(clock.ToBusinessTime(shift.ClockInUtc), clock.ToBusinessTime(clockOut));
        await WriteAuditAsync(shift, StaffAuditActions.ShiftClosed, $"closed: {times}. Reason: {request.Reason.Trim()}", cancellationToken);
    }

    public async Task<int> Handle(AddShiftCommand request, CancellationToken cancellationToken)
    {
        // The id exactly as stored, so "is this my own shift?" cannot be dodged by writing the id differently.
        var person = await directory.GetAsync(request.UserId.Trim(), cancellationToken)
            ?? throw new ValidationException(nameof(AddShiftCommand.UserId), "Choose who worked the shift from the list.");
        await RefuseOwnShiftAsync(person.UserId, cancellationToken);

        var now = TimeClockTime.Now(clock);
        var clockIn = ToUtc(request.ClockInLocal, nameof(AddShiftCommand.ClockInLocal));
        var clockOut = ToUtc(request.ClockOutLocal, nameof(AddShiftCommand.ClockOutLocal), after: clockIn);

        TimeClockShift shift;
        try
        {
            shift = TimeClockShift.AddByHand(person.UserId, person.FullName, clockIn, clockOut, By, request.Reason.Trim(), now);
        }
        catch (DomainException ex)
        {
            throw new ValidationException(ex.Message);
        }

        var outcome = await repository.AddClosedShiftAsync(shift, cancellationToken);
        await ThrowUnlessSavedAsync(outcome, person.UserId, clockIn, clockOut, null, cancellationToken);

        var times = TimeClockText.Times(clock.ToBusinessTime(clockIn), clock.ToBusinessTime(clockOut));
        await WriteAuditAsync(shift, StaffAuditActions.ShiftAdded, $"added by hand: {times}. Reason: {request.Reason.Trim()}", cancellationToken);
        return shift.Id;
    }

    public async Task Handle(VoidShiftCommand request, CancellationToken cancellationToken)
    {
        var shift = await LoadAsync(request.Id, cancellationToken);
        if (shift.IsVoided)
        {
            throw new ValidationException("This shift is already voided.");
        }

        if (shift.IsOpen)
        {
            throw new ValidationException("This shift is still open. Close it first, then void it if it shouldn't count.");
        }

        if (!await repository.VoidAsync(shift.Id, request.Version, By, request.Reason.Trim(), TimeClockTime.Now(clock), cancellationToken))
        {
            throw new ValidationException(ChangedMeanwhile);
        }

        var times = TimeClockText.Times(clock.ToBusinessTime(shift.ClockInUtc), Local(shift.ClockOutUtc));
        await WriteAuditAsync(shift, StaffAuditActions.ShiftVoided, $"voided {times}. Reason: {request.Reason.Trim()}", cancellationToken);
    }

    public async Task Handle(MarkShiftReviewedCommand request, CancellationToken cancellationToken)
    {
        var shift = await LoadAsync(request.Id, cancellationToken);
        if (!shift.NeedsReview)
        {
            return; // Already reviewed (e.g. a second click).
        }

        if (!await repository.MarkReviewedAsync(shift.Id, request.Version, TimeClockTime.Now(clock), cancellationToken))
        {
            throw new ValidationException(ChangedMeanwhile);
        }

        await WriteAuditAsync(shift, StaffAuditActions.ShiftReviewed, "marked as reviewed.", cancellationToken);
    }

    /// <summary>True when both ids name the same account, however they are written (SQL Server compares ids without case).</summary>
    internal static bool SamePerson(string userId, string? otherUserId) =>
        otherUserId is not null && string.Equals(userId.Trim(), otherUserId.Trim(), StringComparison.OrdinalIgnoreCase);

    private async Task<TimeClockShift> LoadAsync(int id, CancellationToken cancellationToken)
    {
        var shift = await repository.GetAsync(id, cancellationToken) ?? throw new NotFoundException("Shift", id);
        await RefuseOwnShiftAsync(shift.UserId, cancellationToken);
        return shift;
    }

    private async Task RefuseOwnShiftAsync(string userId, CancellationToken cancellationToken)
    {
        var access = await currentStaff.GetAsync(cancellationToken);
        if (!access.IsAdmin && SamePerson(userId, access.UserId))
        {
            throw new ValidationException(OwnShiftRefusal);
        }
    }

    private async Task SaveTimesAsync(ShiftTimesChange change, CancellationToken cancellationToken)
    {
        var outcome = await repository.ChangeTimesAsync(change, cancellationToken);
        await ThrowUnlessSavedAsync(outcome, change.UserId, change.ClockInUtc, change.ClockOutUtc, change.ShiftId, cancellationToken);
    }

    private async Task ThrowUnlessSavedAsync(
        ShiftChangeOutcome outcome, string userId, DateTime clockIn, DateTime? clockOut, int? shiftId, CancellationToken cancellationToken)
    {
        switch (outcome)
        {
            case ShiftChangeOutcome.Saved:
                return;
            case ShiftChangeOutcome.Overlaps:
                var other = await repository.FindOverlapAsync(userId, clockIn, clockOut, shiftId, cancellationToken);
                throw new ValidationException(other is null
                    ? "Those times overlap another shift of this person."
                    : $"Those times overlap another shift of this person ({TimeClockText.Times(clock.ToBusinessTime(other.ClockInUtc), Local(other.ClockOutUtc))}). Correct or void that shift first.");
            default:
                throw new ValidationException(ChangedMeanwhile);
        }
    }

    private Task WriteAuditAsync(TimeClockShift shift, string action, string what, CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new StaffAuditEntry(shift.UserId, shift.EmployeeName, action, ShiftAudit.Details(shift.Id, clock.ToBusinessTime(shift.ClockInUtc), what), By),
            cancellationToken);

    /// <summary>The saved time, as the form shows it (business-local, to the minute), is <paramref name="local"/>.</summary>
    private bool ShowsAs(DateTime savedUtc, DateTime local) => ToMinute(clock.ToBusinessTime(savedUtc)) == ToMinute(local);

    private static DateTime ToMinute(DateTime value) => new(value.Ticks - value.Ticks % TimeSpan.TicksPerMinute);

    /// <param name="after">For a clock-out: its clock-in (picks the right one of a time that happened twice).</param>
    private DateTime ToUtc(DateTime local, string field, DateTime? after = null)
    {
        try
        {
            return after is { } clockIn
                ? BusinessTime.FromLocalInput(local, clock.BusinessTimeZone, clockIn)
                : BusinessTime.FromLocalInput(local, clock.BusinessTimeZone);
        }
        catch (DomainException ex)
        {
            throw new ValidationException(field, ex.Message);
        }
    }

    private static void CheckTimes(DateTime clockIn, DateTime? clockOut, DateTime now)
    {
        try
        {
            ShiftRules.CheckTimes(clockIn, clockOut, now);
        }
        catch (DomainException ex)
        {
            throw new ValidationException(ex.Message);
        }
    }

    private DateTime? Local(DateTime? utc) => utc is { } value ? clock.ToBusinessTime(value) : null;
}
