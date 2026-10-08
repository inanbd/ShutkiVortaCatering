using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Web.Seo;

namespace ShutkiVorta.Web.Pages;

public sealed class PrivacyModel(IOptions<BusinessOptions> business) : PageModel
{
    public BusinessOptions Business => business.Value;

    public void OnGet() => ViewData.SetSeo(new SeoMetadata
    {
        Title = "Privacy Policy",
        Description = $"How {business.Value.Name} collects and uses your information when you order.",
        CanonicalPath = "/privacy",
    });
}
