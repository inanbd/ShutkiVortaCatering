using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Inquiries;
using ShutkiVorta.Web.Infrastructure;
using ShutkiVorta.Web.Seo;

namespace ShutkiVorta.Web.Pages;

public sealed class CateringModel(ISender sender, IAppUrls urls, IOptions<BusinessOptions> business) : AppPageModel(sender)
{
    [BindProperty]
    public InquiryInput Input { get; set; } = new();

    public bool Submitted { get; private set; }

    public void OnGet(bool sent = false)
    {
        Submitted = sent;
        SetSeo();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        SetSeo();
        if (Input.LooksLikeSpam)
        {
            return Redirect("/catering?sent=true#inquiry"); // silently drop bot submissions
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var ok = await TryExecuteAsync(
            () => Sender.Send(new SubmitCateringInquiryCommand(Input.Name, Input.Email, Input.Phone, Input.EventDate, Input.GuestCount, Input.Message), cancellationToken),
            typeof(InquiryInput));

        return ok ? Redirect("/catering?sent=true#inquiry") : Page();
    }

    private void SetSeo()
    {
        var b = business.Value;
        var seo = ViewData.SetSeo(new SeoMetadata
        {
            Title = $"Bangladeshi Event Catering in {b.City} — Weddings, Dawat & Eid",
            Description = $"Vorta platters and shutki vorta catering for weddings, dawat, Eid, Pohela Boishakh and family gatherings in {b.City} and the DFW area. Request a quote today.",
            CanonicalPath = "/catering",
            ImageUrl = "/images/site/market.webp",
        });
        seo.StructuredData.Add(StructuredData.Breadcrumbs(urls, ("Home", "/"), ("Catering", "/catering")));
    }
}
