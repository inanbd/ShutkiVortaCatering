using MediatR;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Wholesale;
using ShutkiVorta.Web.Infrastructure;
using ShutkiVorta.Web.Services;
using ShutkiVorta.Web.Seo;

namespace ShutkiVorta.Web.Pages.Restaurants;

/// <summary>Public landing page for restaurants: wholesale prices, how standing orders work, and a call to action.</summary>
public sealed class IndexModel(ISender sender, IAppUrls urls, IOptions<BusinessOptions> business, ICurrentUser currentUser) : AppPageModel(sender)
{
    public WholesaleCatalogDto Catalog { get; private set; } = null!;
    public BusinessOptions Business => business.Value;
    public bool IsSignedIn => currentUser.IsAuthenticated;

    public IReadOnlyList<(string Question, string Answer)> Faq { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Catalog = await Sender.Send(new GetWholesaleCatalogQuery(), cancellationToken);
        var c = Catalog;
        Faq =
        [
            ("How much do I need to order?",
                $"At least {Ui.Number(c.MinimumQuantityPerItem)} lb of each vorta you choose and {Ui.Money(c.MinimumSubtotalPerDelivery)} per delivery. Mix and match as many vortas as you like."),
            ("Which days can you deliver?",
                $"Choose any days of the week{(c.ClosedDays.Count > 0 ? $" except {string.Join(" and ", c.ClosedDays.Select(d => d + "s"))}, when our kitchen is closed" : string.Empty)}. Pick a delivery time that suits your prep schedule."),
            ("Can I skip a day or pause for a holiday?",
                $"Yes. Sign in, open your standing order and skip any delivery up to {c.ChangeCutoffHours} hours before it, or pause the whole order and resume later. You can also simply call us."),
            ("Do you charge sales tax?",
                "Restaurants that resell our vortas can send us a Texas Sales and Use Tax Resale Certificate (Form 01-339). Once it is on file we remove sales tax from your deliveries."),
            ("How do I pay?", c.PaymentTerms),
            ("How soon can we start?",
                $"Your first delivery can be {c.LeadTimeDays} days after you send the request. We confirm every new restaurant personally, usually within one business day."),
        ];

        var b = business.Value;
        var seo = ViewData.SetSeo(new SeoMetadata
        {
            Title = $"Wholesale Bangladeshi Vortas for Restaurants in {b.City}",
            Description = $"Fresh shutki and homestyle vortas delivered to your restaurant in {b.City} and DFW on the days you choose. Wholesale prices by the pound, standing weekly orders, skip or pause online.",
            CanonicalPath = "/restaurants",
            ImageUrl = "/images/site/assorted.webp",
        });
        seo.StructuredData.Add(StructuredData.Breadcrumbs(urls, ("Home", "/"), ("Restaurants", "/restaurants")));
        seo.StructuredData.Add(StructuredData.Faq(Faq));
    }
}
