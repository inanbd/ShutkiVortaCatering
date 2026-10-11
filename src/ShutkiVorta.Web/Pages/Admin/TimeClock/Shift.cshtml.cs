using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.TimeClock;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.TimeClock;

/// <summary>
/// One shift: times, both locations, notes and history. Anyone may open their own; people who see the team's shifts may open
/// any (others get "not found", so ids can't be probed). Managers fix other people's shifts here.
/// </summary>
[AnyStaff]
public sealed class ShiftModel(ISender sender) : AppPageModel(sender)
{
    [BindProperty]
    public TimesInput Times { get; set; } = new();

    [BindProperty]
    public CloseInput Closing { get; set; } = new();

    [BindProperty]
    public VoidInput Voiding { get; set; } = new();

    /// <summary>The version the page was loaded with, posted back by every action.</summary>
    [BindProperty]
    public int Version { get; set; }

    public ShiftDetailsDto Details { get; private set; } = null!;

    public ShiftDto Shift => Details.Shift;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        Version = Shift.Version;
        Times = new TimesInput { ClockIn = Shift.ClockInLocal, ClockOut = Shift.IsMissedClockOut ? null : Shift.ClockOutLocal };
        return Page();
    }

    [RequirePermission(Permissions.TimeClockManage)]
    public Task<IActionResult> OnPostCorrectAsync(int id, CancellationToken cancellationToken)
    {
        if (Times.ClockIn is not { } clockIn)
        {
            ModelState.AddModelError("Times.ClockIn", "Enter when the shift started.");
            return ShowAsync(id, cancellationToken);
        }

        return ChangeAsync(id, () => Sender.Send(new CorrectShiftCommand(id, Version, clockIn, Times.ClockOut, Times.Reason ?? string.Empty), cancellationToken),
            "Shift corrected.", cancellationToken);
    }

    [RequirePermission(Permissions.TimeClockManage)]
    public Task<IActionResult> OnPostCloseAsync(int id, CancellationToken cancellationToken)
    {
        if (Closing.ClockOut is not { } clockOut)
        {
            ModelState.AddModelError("Closing.ClockOut", "Enter when the person left.");
            return ShowAsync(id, cancellationToken);
        }

        return ChangeAsync(id, () => Sender.Send(new CloseShiftCommand(id, Version, clockOut, Closing.Reason ?? string.Empty), cancellationToken),
            "Shift closed.", cancellationToken);
    }

    [RequirePermission(Permissions.TimeClockManage)]
    public Task<IActionResult> OnPostVoidAsync(int id, CancellationToken cancellationToken) =>
        ChangeAsync(id, () => Sender.Send(new VoidShiftCommand(id, Version, Voiding.Reason ?? string.Empty), cancellationToken),
            "Shift voided: it no longer counts.", cancellationToken);

    [RequirePermission(Permissions.TimeClockManage)]
    public Task<IActionResult> OnPostReviewedAsync(int id, CancellationToken cancellationToken) =>
        ChangeAsync(id, () => Sender.Send(new MarkShiftReviewedCommand(id, Version), cancellationToken), "Marked as reviewed.", cancellationToken);

    private async Task<IActionResult> ChangeAsync(int id, Func<Task> change, string message, CancellationToken cancellationToken)
    {
        if (await TryExecuteAsync(change))
        {
            StatusMessage = message;
            return Redirect(TimeClockUi.ShiftPath(id));
        }

        return await ShowAsync(id, cancellationToken);
    }

    /// <summary>The page again, with the errors and what was typed.</summary>
    private async Task<IActionResult> ShowAsync(int id, CancellationToken cancellationToken) =>
        await LoadAsync(id, cancellationToken) ? Page() : NotFound();

    private async Task<bool> LoadAsync(int id, CancellationToken cancellationToken)
    {
        var details = await Sender.Send(new GetShiftQuery(id), cancellationToken);
        if (details is null)
        {
            return false;
        }

        Details = details;
        ViewData["Title"] = $"Shift · {details.Shift.EmployeeName}";
        return true;
    }

    public sealed class TimesInput
    {
        public DateTime? ClockIn { get; set; }
        public DateTime? ClockOut { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class CloseInput
    {
        public DateTime? ClockOut { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class VoidInput
    {
        public string? Reason { get; set; }
    }
}
