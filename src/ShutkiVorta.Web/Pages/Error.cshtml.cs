using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShutkiVorta.Web.Seo;

namespace ShutkiVorta.Web.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public sealed class ErrorModel : PageModel
{
    public int Code { get; private set; } = 500;
    public string? RequestId { get; private set; }

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
