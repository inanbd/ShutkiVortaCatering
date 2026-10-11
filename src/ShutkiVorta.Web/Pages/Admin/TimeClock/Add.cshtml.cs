using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.TimeClock;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.TimeClock;

/// <summary>Adds a shift for someone who could not clock in (dead phone, no signal…), with a reason.</summary>
[RequirePermission(Permissions.TimeClockManage)]
public sealed class AddModel(ISender sender, ICurrentStaff currentStaff, IDateTimeProvider clock) : AppPageModel(sender)
{
    [BindProperty]
    public AddInput Input { get; set; } = new();

    /// <summary>Who the shift can be for: staff and former staff (not yourself, unless you are an Admin).</summary>
    public IReadOnlyList<TimeClockPersonDto> People { get; private set; } = [];

    public async Task OnGetAsync(string? user, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.BusinessNow);
        Input = new AddInput
        {
            UserId = user,
            ClockIn = today.ToDateTime(new TimeOnly(9, 0)),
            ClockOut = today.ToDateTime(new TimeOnly(17, 0)),
        };
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (Input.ClockIn is null)
        {
            ModelState.AddModelError("Input.ClockIn", "Enter when the shift started.");
        }

        if (Input.ClockOut is null)
        {
            ModelState.AddModelError("Input.ClockOut", "Enter when the shift ended.");
        }

        var id = 0;
        if (ModelState.IsValid && await TryExecuteAsync(async () => id = await Sender.Send(
                new AddShiftCommand(Input.UserId ?? string.Empty, Input.ClockIn!.Value, Input.ClockOut!.Value, Input.Reason ?? string.Empty), cancellationToken),
                typeof(AddInput)))
        {
            StatusMessage = "Shift added.";
            return Redirect(TimeClockUi.ShiftPath(id));
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Add a shift";
        var access = await currentStaff.GetAsync(cancellationToken);
        var team = await Sender.Send(new GetTeamTimeClockQuery(), cancellationToken);
        People = [.. team.People.Where(p => access.IsAdmin || !string.Equals(p.UserId, access.UserId, StringComparison.OrdinalIgnoreCase))];
    }

    public sealed class AddInput
    {
        public string? UserId { get; set; }
        public DateTime? ClockIn { get; set; }
        public DateTime? ClockOut { get; set; }
        public string? Reason { get; set; }
    }
}
