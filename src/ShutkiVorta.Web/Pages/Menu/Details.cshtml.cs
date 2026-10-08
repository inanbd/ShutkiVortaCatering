using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Web.Seo;
using ShutkiVorta.Web.Services;

namespace ShutkiVorta.Web.Pages.Menu;

public sealed class DetailsModel(
    ISender sender,
    IAppUrls urls,
    CartService cart,
    IOptions<BusinessOptions> business,
    IOptions<OrderingOptions> ordering) : PageModel
{
    public MenuItemDto Item { get; private set; } = null!;
    public IReadOnlyList<MenuItemDto> Related { get; private set; } = [];
    public decimal InCart { get; private set; }
    public BusinessOptions Business => business.Value;
    public OrderingOptions Ordering => ordering.Value;

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken cancellationToken)
    {
        var item = await sender.Send(new GetMenuItemBySlugQuery(slug), cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        // Canonicalise the URL (e.g. upper-case slugs) with a permanent redirect.
        if (!string.Equals(slug, item.Slug, StringComparison.Ordinal))
        {
            return RedirectPermanent($"/menu/{item.Slug}");
        }

        Item = item;
        InCart = cart.QuantityOf(item.Id);
        var menu = await sender.Send(new GetMenuQuery(), cancellationToken);
        Related = menu.Where(i => i.Id != item.Id)
            .OrderBy(i => i.Category == item.Category ? 1 : 0)
            .ThenByDescending(i => i.IsFeatured)
            .ThenBy(i => i.SortOrder)
            .Take(3)
            .ToList();

        var b = business.Value;
        var seo = ViewData.SetSeo(new SeoMetadata
        {
            Title = item.MetaTitle ?? $"{item.Name} by the Pound in {b.City}, {b.State}",
            Description = item.MetaDescription ?? item.ShortDescription,
            CanonicalPath = $"/menu/{item.Slug}",
            ImageUrl = item.ImageUrl,
            OpenGraphType = "product",
        });
        seo.StructuredData.Add(StructuredData.Product(item, b, urls));
        seo.StructuredData.Add(StructuredData.Breadcrumbs(urls, ("Home", "/"), ("Menu", "/menu"), (item.Name, $"/menu/{item.Slug}")));
        return Page();
    }
}
