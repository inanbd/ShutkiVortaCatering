using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Web.Seo;
using ShutkiVorta.Web.Services;

namespace ShutkiVorta.Web.Pages;

public sealed class FaqModel(IAppUrls urls, IOptions<BusinessOptions> business, IOptions<OrderingOptions> ordering) : PageModel
{
    public IReadOnlyList<(string Question, string Answer)> Entries { get; private set; } = [];

    public void OnGet()
    {
        var b = business.Value;
        var o = ordering.Value;
        Entries =
        [
            ("What is shutki vorta?",
                "Shutki vorta is a traditional Bangladeshi dish of sun-dried fish (shutki) that is roasted and pounded with onion, garlic, chilies and mustard oil. It is eaten with plain rice and is one of the most loved comfort foods in Bangladesh."),
            ("How do I order vorta by the pound?",
                $"Choose your vortas on our menu, pick a quantity in half-pound steps and add them to your order. At checkout choose pickup or delivery and a date and time at least {o.MinimumLeadTimeHours} hours ahead."),
            ("How much vorta should I order?",
                "A half pound of vorta serves two to three people as a side with rice. For parties we suggest about a quarter pound per guest for each vorta you serve."),
            ("Where do I pick up my order?",
                $"Pickup is from our kitchen in {b.City}, {b.State}. {b.PickupInstructions}"),
            ("Do you deliver?",
                $"Yes. We deliver to {o.DeliveryAreaDescription}. Delivery costs {Ui.Money(o.DeliveryFee)}" +
                (o.FreeDeliveryThreshold is { } t ? $" and is free for orders over {Ui.Money(t)}" : string.Empty) +
                $". Delivery orders need a minimum food subtotal of {Ui.Money(o.MinimumDeliverySubtotal)}."),
            ("How do I pay?", o.PaymentInstructions),
            ("How long does vorta keep?",
                "Keep vortas refrigerated in a closed container and enjoy within 3–4 days. Let them come to room temperature before serving for the best aroma. Shutki vortas also freeze well for up to a month."),
            ("Can I make the vorta milder or spicier?",
                "Absolutely. Add a note at checkout and we will adjust the chilies for you."),
            ("Can I cancel or change my order?",
                $"You can cancel online until we confirm your order using the link in your confirmation email. After that, please call us at {b.Phone} and we will do our best to help."),
            ("Do you cater events?",
                "Yes — weddings, dawat, Eid gatherings, Pohela Boishakh and corporate events. Send us a catering inquiry and we will reply with ideas and a quote."),
        ];

        var seo = ViewData.SetSeo(new SeoMetadata
        {
            Title = $"FAQ — Ordering Shutki Vorta in {b.City}",
            Description = $"Answers about ordering Bangladeshi vorta by the pound in {b.City}: pickup, delivery, payment, portion sizes, storage and catering.",
            CanonicalPath = "/faq",
        });
        seo.StructuredData.Add(StructuredData.Faq(Entries));
        seo.StructuredData.Add(StructuredData.Breadcrumbs(urls, ("Home", "/"), ("FAQ", "/faq")));
    }
}
