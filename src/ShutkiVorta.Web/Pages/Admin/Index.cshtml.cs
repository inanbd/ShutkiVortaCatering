using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Dashboard;
using ShutkiVorta.Application.Features.TimeClock;
using ShutkiVorta.Web.Infrastructure.Security;

namespace ShutkiVorta.Web.Pages.Admin;

/// <summary>The dashboard, or the time clock for staff who may not see the dashboard (so /admin always works as home).</summary>
[AnyStaff]
public sealed class IndexModel(ISender sender, ICurrentStaff currentStaff, ITimeClockStatusReader timeClock, IDateTimeProvider clock) : PageModel
{
    public DashboardDto Dashboard { get; private set; } = null!;

    /// <summary>When the signed-in person's open shift started, e.g. "9:02 AM" (or "9:02 AM on Mon, Oct 5"); null when off the clock.</summary>
    public string? OnClockSince { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var access = await currentStaff.GetAsync(cancellationToken);
        if (!access.Can(Permissions.DashboardView))
        {
            return Redirect(StaffLanding.TimeClock);
        }

        ViewData["Title"] = "Dashboard";
        Dashboard = await sender.Send(new GetDashboardQuery(), cancellationToken);
        if (await timeClock.GetOpenShiftStartAsync(access.UserId!, cancellationToken) is { } startUtc)
        {
            var start = clock.ToBusinessTime(startUtc);
            OnClockSince = DateOnly.FromDateTime(start) == DateOnly.FromDateTime(clock.BusinessNow)
                ? Format.Time(start)
                : $"{Format.Time(start)} on {start.ToString("ddd, MMM d", Format.Culture)}";
        }

        return Page();
    }
}
