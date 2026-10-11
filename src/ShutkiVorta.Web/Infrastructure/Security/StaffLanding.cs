using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.Web.Infrastructure.Security;

/// <summary>Where a staff member starts: the dashboard when they may see it, otherwise the time clock.</summary>
public static class StaffLanding
{
    public const string Dashboard = "/admin";
    public const string TimeClock = "/admin/time-clock";

    public static string For(StaffAccess access) => access.Can(Permissions.DashboardView) ? Dashboard : TimeClock;
}
