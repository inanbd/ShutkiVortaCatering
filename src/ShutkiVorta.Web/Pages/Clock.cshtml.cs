using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShutkiVorta.Web.Infrastructure.Security;

namespace ShutkiVorta.Web.Pages;

/// <summary>
/// A short address staff can save on their phone's home screen: /clock opens the time clock (the admin area asks people to
/// sign in first). Hidden from search engines in robots.txt.
/// </summary>
public sealed class ClockModel : PageModel
{
    public IActionResult OnGet() => LocalRedirect(StaffLanding.TimeClock);
}
