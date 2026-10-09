using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Inquiries;
using ShutkiVorta.Domain.Inquiries;
using ShutkiVorta.Web.Infrastructure;
using ShutkiVorta.Web.Seo;

namespace ShutkiVorta.Web.Pages;

/// <summary>"Our Kitchen": vortas made by Bangladeshi mothers in an inspected kitchen, plus a form for homemakers who want to cook with us.</summary>
public sealed class KitchenModel(ISender sender, IAppUrls urls, IOptions<BusinessOptions> business) : AppPageModel(sender)
{
    private const string SentUrl = "/kitchen?sent=true#join";

    [BindProperty]
    public JoinKitchenInput Input { get; set; } = new();

    public bool Submitted { get; private set; }
    public BusinessOptions Business => business.Value;

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
            return Redirect(SentUrl); // silently drop bot submissions
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var ok = await TryExecuteAsync(
            () => Sender.Send(new SubmitCateringInquiryCommand(Input.Name, Input.Email, Input.Phone, null, null, Input.Message, Topic: InquiryTopic.JoinKitchen), cancellationToken),
            typeof(JoinKitchenInput));

        return ok ? Redirect(SentUrl) : Page();
    }

    private void SetSeo()
    {
        var b = business.Value;
        var seo = ViewData.SetSeo(new SeoMetadata
        {
            Title = $"Our Kitchen — Vortas Made by Bangladeshi Mothers in {b.City}",
            Description = $"Our vortas are made by Bangladeshi mothers and homemakers from the {b.ServiceArea} community: home-style recipes from every district, roasted and pounded by hand in our health-inspected {b.City} kitchen.",
            CanonicalPath = "/kitchen",
            ImageUrl = "/images/kitchen/mother-at-clay-stove.webp",
        });
        seo.StructuredData.Add(StructuredData.Breadcrumbs(urls, ("Home", "/"), ("Our Kitchen", "/kitchen")));
    }
}
