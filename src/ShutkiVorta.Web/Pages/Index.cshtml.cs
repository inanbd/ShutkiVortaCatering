using MediatR;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Domain.Menu;
using ShutkiVorta.Web.Seo;

namespace ShutkiVorta.Web.Pages;

public sealed class IndexModel(ISender sender, IAppUrls urls, IOptions<BusinessOptions> business, IOptions<OrderingOptions> ordering) : PageModel
{
    public IReadOnlyList<MenuItemDto> ShutkiItems { get; private set; } = [];
    public IReadOnlyList<MenuItemDto> ClassicItems { get; private set; } = [];
    public BusinessOptions Business => business.Value;
    public OrderingOptions Ordering => ordering.Value;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var menu = await sender.Send(new GetMenuQuery(), cancellationToken);
        ShutkiItems = menu.Where(i => i.Category == MenuCategory.ShutkiVorta).ToList();
        ClassicItems = menu.Where(i => i.Category == MenuCategory.ClassicVorta).ToList();

        var seo = ViewData.SetSeo(new SeoMetadata
        {
            Title = $"Shutki Vorta & Bangladeshi Vorta Catering in {Business.City}, {Business.State} | {Business.Name}",
            ExactTitle = true,
            Description = $"Order authentic Bangladeshi shutki vorta — loitta, chingri and chepa — plus aloo, begun and dal vorta by the pound in {Business.City}, TX. Made fresh to order for pickup or local delivery.",
            CanonicalPath = "/",
            ImageUrl = "/images/og-default.jpg",
        });
        seo.StructuredData.Add(StructuredData.Business(Business, urls));
        seo.StructuredData.Add(StructuredData.WebSite(Business, urls));
    }
}
