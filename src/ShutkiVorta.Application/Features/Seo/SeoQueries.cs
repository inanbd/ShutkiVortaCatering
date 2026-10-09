using System.Globalization;
using System.Text;
using System.Xml;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Menu;

namespace ShutkiVorta.Application.Features.Seo;

/// <summary>XML sitemap listing every public page and menu item (regenerated automatically when the menu changes).</summary>
public sealed record GetSitemapQuery : IRequest<string>;

/// <summary>robots.txt content (regenerated automatically when the menu changes).</summary>
public sealed record GetRobotsTxtQuery : IRequest<string>;

public static class SeoPaths
{
    /// <summary>Public, indexable pages that are not generated from the menu.</summary>
    public static readonly IReadOnlyList<(string Path, string ChangeFrequency, string Priority)> StaticPages =
    [
        ("/", "weekly", "1.0"),
        ("/menu", "weekly", "0.9"),
        ("/restaurants", "monthly", "0.8"),
        ("/catering", "monthly", "0.7"),
        ("/kitchen", "monthly", "0.7"),
        ("/about", "monthly", "0.6"),
        ("/faq", "monthly", "0.6"),
        ("/contact", "monthly", "0.5"),
    ];

    /// <summary>Private or transactional areas that search engines should not crawl.</summary>
    public static readonly IReadOnlyList<string> Disallowed =
    [
        "/admin",
        "/account",
        "/cart",
        "/checkout",
        "/restaurants/order",
        "/order/",
        "/track-order",
        "/error",
    ];
}

internal static class SeoCacheKeys
{
    public const string Sitemap = "seo:sitemap.xml";
    public const string Robots = "seo:robots.txt";
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);
}

internal sealed class SeoQueryHandlers(
    IMenuItemRepository menu,
    IAppUrls urls,
    IDateTimeProvider clock,
    IMemoryCache cache,
    ISettingsChangeSignal settingsChanged,
    IOptions<SiteOptions> site,
    IOptions<BusinessOptions> business) :
    IRequestHandler<GetSitemapQuery, string>,
    IRequestHandler<GetRobotsTxtQuery, string>
{
    private static readonly XmlWriterSettings WriterSettings = new()
    {
        Encoding = new UTF8Encoding(false),
        Indent = true,
        Async = true,
    };

    public async Task<string> Handle(GetSitemapQuery request, CancellationToken cancellationToken) =>
        (await cache.GetOrCreateAsync(SeoCacheKeys.Sitemap, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = SeoCacheKeys.Lifetime;
            entry.AddExpirationToken(settingsChanged.GetChangeToken());
            return await BuildSitemapAsync(cancellationToken);
        }))!;

    public async Task<string> Handle(GetRobotsTxtQuery request, CancellationToken cancellationToken) =>
        (await cache.GetOrCreateAsync(SeoCacheKeys.Robots, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = SeoCacheKeys.Lifetime;
            entry.AddExpirationToken(settingsChanged.GetChangeToken());
            return await BuildRobotsAsync(cancellationToken);
        }))!;

    private async Task<string> BuildSitemapAsync(CancellationToken cancellationToken)
    {
        const string ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        const string imageNs = "http://www.google.com/schemas/sitemap-image/1.1";

        var items = await menu.GetAllAsync(includeUnavailable: true, cancellationToken);
        var menuLastModified = items.Count == 0 ? (DateTime?)null : items.Max(i => i.UpdatedAtUtc);

        var builder = new StringBuilder();
        await using (var stringWriter = new StringWriterWithEncoding(builder, Encoding.UTF8))
        await using (var xml = XmlWriter.Create(stringWriter, WriterSettings))
        {
            await xml.WriteStartDocumentAsync();
            xml.WriteStartElement("urlset", ns);
            xml.WriteAttributeString("xmlns", "image", null, imageNs);

            foreach (var (path, changeFrequency, priority) in SeoPaths.StaticPages)
            {
                var lastModified = path is "/" or "/menu" ? menuLastModified : null;
                WriteUrl(xml, ns, urls.Absolute(path), lastModified, changeFrequency, priority, imageUrl: null, imageTitle: null, imageNs);
            }

            foreach (var item in items.OrderBy(i => i.Category).ThenBy(i => i.SortOrder))
            {
                var image = string.IsNullOrWhiteSpace(item.ImageUrl) ? null : ToAbsolute(item.ImageUrl);
                WriteUrl(xml, ns, urls.MenuItem(item.Slug), item.UpdatedAtUtc, "weekly", item.IsAvailable ? "0.8" : "0.4", image, item.Name, imageNs);
            }

            xml.WriteEndElement();
            await xml.WriteEndDocumentAsync();
        }

        return builder.ToString();
    }

    private async Task<string> BuildRobotsAsync(CancellationToken cancellationToken)
    {
        var options = site.Value;
        var sb = new StringBuilder();
        sb.AppendLine($"# robots.txt for {business.Value.Name}");
        sb.AppendLine($"# Generated automatically on {clock.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)}. Updated whenever the menu changes.");
        sb.AppendLine();

        if (!options.AllowSearchEngineIndexing)
        {
            sb.AppendLine("# Indexing is disabled (Site:AllowSearchEngineIndexing = false).");
            sb.AppendLine("User-agent: *");
            sb.AppendLine("Disallow: /");
            return sb.ToString();
        }

        var items = await menu.GetAllAsync(includeUnavailable: true, cancellationToken);

        sb.AppendLine("User-agent: *");
        sb.AppendLine("Allow: /");
        foreach (var path in SeoPaths.StaticPages.Select(p => p.Path).Where(p => p != "/"))
        {
            sb.AppendLine($"Allow: {path}");
        }

        sb.AppendLine();
        sb.AppendLine($"# Menu item pages ({items.Count})");
        foreach (var item in items.OrderBy(i => i.Category).ThenBy(i => i.SortOrder))
        {
            sb.AppendLine($"Allow: /menu/{item.Slug}");
        }

        sb.AppendLine();
        sb.AppendLine("# Private and transactional pages");
        foreach (var path in SeoPaths.Disallowed.Concat(options.AdditionalDisallowedPaths).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"Disallow: {path}");
        }

        sb.AppendLine();
        sb.AppendLine($"Sitemap: {urls.Absolute("/sitemap.xml")}");
        return sb.ToString();
    }

    private string ToAbsolute(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? url
            : urls.Absolute(url);

    private static void WriteUrl(
        XmlWriter xml, string ns, string loc, DateTime? lastModifiedUtc, string changeFrequency, string priority,
        string? imageUrl, string? imageTitle, string imageNs)
    {
        xml.WriteStartElement("url", ns);
        xml.WriteElementString("loc", ns, loc);
        if (lastModifiedUtc is { } lm)
        {
            xml.WriteElementString("lastmod", ns, DateTime.SpecifyKind(lm, DateTimeKind.Utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        xml.WriteElementString("changefreq", ns, changeFrequency);
        xml.WriteElementString("priority", ns, priority);

        if (imageUrl is not null)
        {
            xml.WriteStartElement("image", "image", imageNs);
            xml.WriteElementString("image", "loc", imageNs, imageUrl);
            if (imageTitle is not null)
            {
                xml.WriteElementString("image", "title", imageNs, imageTitle);
            }

            xml.WriteEndElement();
        }

        xml.WriteEndElement();
    }

    private sealed class StringWriterWithEncoding(StringBuilder builder, Encoding encoding) : StringWriter(builder, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => encoding;
    }
}

/// <summary>Drops cached sitemap/robots content as soon as a menu item or a website/business setting changes.</summary>
internal sealed class SeoCacheInvalidationHandler(IMemoryCache cache) :
    INotificationHandler<MenuChangedNotification>,
    INotificationHandler<Settings.SettingsChangedNotification>
{
    public Task Handle(MenuChangedNotification notification, CancellationToken cancellationToken) => Clear();

    public Task Handle(Settings.SettingsChangedNotification notification, CancellationToken cancellationToken) => Clear();

    private Task Clear()
    {
        cache.Remove(SeoCacheKeys.Sitemap);
        cache.Remove(SeoCacheKeys.Robots);
        return Task.CompletedTask;
    }
}
