using MediatR;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Web.Seo;

namespace ShutkiVorta.Web.Pages.Menu;

public sealed class IndexModel(ISender sender, IAppUrls urls, IOptions<BusinessOptions> business, IOptions<OrderingOptions> ordering) : PageModel
{
    public IReadOnlyList<IGrouping<Domain.Menu.MenuCategory, MenuItemDto>> Sections { get; private set; } = [];
    public OrderingOptions Ordering => ordering.Value;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var items = await sender.Send(new GetMenuQuery(), cancellationToken);
        Sections = items.GroupBy(i => i.Category).OrderBy(g => g.Key).ToList();

        var b = business.Value;
        var seo = ViewData.SetSeo(new SeoMetadata
        {
            Title = $"Menu — Shutki Vorta & Bangladeshi Vortas by the Pound in {b.City}",
            Description = $"Browse our menu of authentic Bangladeshi vortas: loitta, chingri and chepa shutki vorta, plus aloo, begun, dal, tomato, kalojira and shim vorta. Order by the lb in {b.City}, TX.",
            CanonicalPath = "/menu",
        });
        seo.StructuredData.Add(StructuredData.Menu(items, b, urls));
        seo.StructuredData.Add(StructuredData.Breadcrumbs(urls, ("Home", "/"), ("Menu", "/menu")));
    }
}
