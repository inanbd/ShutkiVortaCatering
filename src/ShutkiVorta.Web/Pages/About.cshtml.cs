using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Web.Seo;

namespace ShutkiVorta.Web.Pages;

public sealed class AboutModel(IAppUrls urls, IOptions<BusinessOptions> business) : PageModel
{
    public BusinessOptions Business => business.Value;

    public void OnGet()
    {
        var b = business.Value;
        var seo = ViewData.SetSeo(new SeoMetadata
        {
            Title = $"Our Story — Bangladeshi Home Cooking in {b.City}",
            Description = $"How a love for shutki and handmade vortas became {b.Name}: traditional Bangladeshi recipes, shil-pata techniques and mustard oil, made fresh in {b.City}, Texas.",
            CanonicalPath = "/about",
            ImageUrl = "/images/site/dried-fish.webp",
        });
        seo.StructuredData.Add(StructuredData.Breadcrumbs(urls, ("Home", "/"), ("Our Story", "/about")));
    }
}
