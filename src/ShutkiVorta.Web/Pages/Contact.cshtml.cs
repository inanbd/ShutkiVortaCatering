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

public sealed class ContactModel(ISender sender, IAppUrls urls, IOptions<BusinessOptions> business) : AppPageModel(sender)
{
    [BindProperty]
    public InquiryInput Input { get; set; } = new();

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
            return Redirect("/contact?sent=true"); // silently drop bot submissions
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var ok = await TryExecuteAsync(
            () => Sender.Send(new SubmitCateringInquiryCommand(Input.Name, Input.Email, Input.Phone, null, null, Input.Message, InquiryTopic.General), cancellationToken),
            typeof(InquiryInput));

        return ok ? Redirect("/contact?sent=true") : Page();
    }

    private void SetSeo()
    {
        var b = business.Value;
        var seo = ViewData.SetSeo(new SeoMetadata
        {
            Title = $"Contact Us — {b.Name}, {b.City}, {b.State}",
            Description = $"Questions about shutki vorta orders, pickup or delivery in {b.City}? Call {b.Phone}, email {b.Email} or send us a message.",
            CanonicalPath = "/contact",
        });
        seo.StructuredData.Add(StructuredData.Business(b, urls));
    }
}
