using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Web.Seo;

namespace ShutkiVorta.Web.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public sealed class ErrorModel : PageModel
{
    public int Code { get; private set; } = 500;
    public string? RequestId { get; private set; }

    /// <summary>
    /// A signed-in staff member refused part of the admin area. Taken from the cookie's role claims: it only changes the
    /// wording and links, and the error page must work even when the database does not.
    /// </summary>
    public bool IsStaffRefusal => Code == 403 && Roles.Staff.Any(User.IsInRole);

    public void OnGet(int? code) => Setup(code);

    public void OnPost(int? code) => Setup(code);

    private void Setup(int? code)
    {
        Code = code ?? 500;
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        Response.StatusCode = Code;
        ViewData.SetSeo(new SeoMetadata { Title = Code == 404 ? "Page not found" : "Something went wrong", NoIndex = true });
    }
}
