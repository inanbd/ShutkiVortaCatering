using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Application.Features.Seo;
using ShutkiVorta.UnitTests.TestDoubles;

namespace ShutkiVorta.UnitTests.Application;

public sealed class SeoQueryTests
{
    private readonly IMenuItemRepository _menu = Substitute.For<IMenuItemRepository>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly SiteOptions _site = new();

    public SeoQueryTests() =>
        _menu.GetAllAsync(true, Arg.Any<CancellationToken>()).Returns([TestData.Item(1), TestData.Item(2, "Aloo Vorta", 11.99m)]);

    private SeoQueryHandlers CreateHandler() =>
        new(_menu, new FakeUrls(), new FakeClock(TestData.Now), _cache, Options.Create(_site), Options.Create(new BusinessOptions()));

    [Fact]
    public async Task Sitemap_ListsStaticPagesAndEveryMenuItem()
    {
        var xml = await CreateHandler().Handle(new GetSitemapQuery(), CancellationToken.None);
        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locations = doc.Descendants(ns + "loc").Select(e => e.Value).ToList();

        Assert.Contains("https://shutki.test/", locations);
        Assert.Contains("https://shutki.test/menu", locations);
        Assert.Contains("https://shutki.test/menu/loitta-shutki-vorta", locations);
        Assert.Contains("https://shutki.test/menu/aloo-vorta", locations);
        Assert.DoesNotContain(locations, l => l.Contains("/admin") || l.Contains("/cart"));
    }

    [Fact]
    public async Task Robots_AllowsMenuPages_BlocksPrivateAreas_AndPointsToSitemap()
    {
        var robots = await CreateHandler().Handle(new GetRobotsTxtQuery(), CancellationToken.None);

        Assert.Contains("Allow: /menu/aloo-vorta", robots);
        Assert.Contains("Disallow: /admin", robots);
        Assert.Contains("Disallow: /checkout", robots);
        Assert.Contains("Sitemap: https://shutki.test/sitemap.xml", robots);
    }

    [Fact]
    public async Task Robots_BlocksEverything_WhenIndexingDisabled()
    {
        _site.AllowSearchEngineIndexing = false;
        var robots = await CreateHandler().Handle(new GetRobotsTxtQuery(), CancellationToken.None);

        Assert.Contains("Disallow: /", robots);
        Assert.DoesNotContain("Sitemap:", robots);
    }

    [Fact]
    public async Task MenuChange_InvalidatesCachedRobots()
    {
        var handler = CreateHandler();
        await handler.Handle(new GetRobotsTxtQuery(), CancellationToken.None);

        _menu.GetAllAsync(true, Arg.Any<CancellationToken>()).Returns([TestData.Item(3, "Shim Vorta", 13.99m)]);
        var stale = await handler.Handle(new GetRobotsTxtQuery(), CancellationToken.None);
        Assert.DoesNotContain("shim-vorta", stale);

        await new SeoCacheInvalidationHandler(_cache).Handle(new MenuChangedNotification(3), CancellationToken.None);
        var fresh = await handler.Handle(new GetRobotsTxtQuery(), CancellationToken.None);
        Assert.Contains("Allow: /menu/shim-vorta", fresh);
    }
}
