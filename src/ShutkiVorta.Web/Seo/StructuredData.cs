using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Menu;

namespace ShutkiVorta.Web.Seo;

/// <summary>schema.org JSON-LD builders (LocalBusiness, Product, Menu, BreadcrumbList, FAQPage).</summary>
public static class StructuredData
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.Default, // escapes <, > and & so the JSON is safe inside a <script> tag
        WriteIndented = false,
    };

    public static string Serialize(object data) => JsonSerializer.Serialize(data, JsonOptions);

    public static Dictionary<string, object?> Business(BusinessOptions business, IAppUrls urls)
    {
        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "FoodEstablishment",
            ["@id"] = urls.Home() + "#business",
            ["name"] = business.Name,
            ["alternateName"] = business.BengaliName,
            ["description"] = business.Tagline,
            ["url"] = urls.Home(),
            ["telephone"] = business.Phone,
            ["email"] = business.Email,
            ["image"] = urls.Absolute("/images/og-default.jpg"),
            ["logo"] = urls.Absolute("/images/logo-mark.svg"),
            ["priceRange"] = business.PriceRange,
            ["servesCuisine"] = new[] { "Bangladeshi", "Bengali" },
            ["hasMenu"] = urls.Menu(),
            ["acceptsReservations"] = "False",
            ["areaServed"] = business.ServiceArea,
            ["address"] = new Dictionary<string, object?>
            {
                ["@type"] = "PostalAddress",
                ["streetAddress"] = string.IsNullOrWhiteSpace(business.StreetAddress) ? null : business.StreetAddress,
                ["addressLocality"] = business.City,
                ["addressRegion"] = business.State,
                ["postalCode"] = business.PostalCode,
                ["addressCountry"] = business.Country,
            },
            ["sameAs"] = new[] { business.FacebookUrl, business.InstagramUrl }.Where(u => !string.IsNullOrWhiteSpace(u)).ToArray(),
        };

        if (business.Latitude is { } lat && business.Longitude is { } lng)
        {
            data["geo"] = new Dictionary<string, object?> { ["@type"] = "GeoCoordinates", ["latitude"] = lat, ["longitude"] = lng };
        }

        return data;
    }

    public static Dictionary<string, object?> WebSite(BusinessOptions business, IAppUrls urls) => new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "WebSite",
        ["name"] = business.Name,
        ["alternateName"] = business.BengaliName,
        ["url"] = urls.Home(),
    };

    public static Dictionary<string, object?> Product(MenuItemDto item, BusinessOptions business, IAppUrls urls) => new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "Product",
        ["name"] = item.Name,
        ["alternateName"] = item.BengaliName,
        ["description"] = item.ShortDescription,
        ["image"] = string.IsNullOrWhiteSpace(item.ImageUrl) ? null : new[] { urls.Absolute(item.ImageUrl) },
        ["sku"] = item.Slug,
        ["category"] = $"{item.CategoryName} — Bangladeshi food",
        ["brand"] = new Dictionary<string, object?> { ["@type"] = "Brand", ["name"] = business.Name },
        ["offers"] = new Dictionary<string, object?>
        {
            ["@type"] = "Offer",
            ["url"] = urls.MenuItem(item.Slug),
            ["priceCurrency"] = "USD",
            ["price"] = item.PricePerUnit,
            ["availability"] = item.IsAvailable ? "https://schema.org/InStock" : "https://schema.org/OutOfStock",
            ["itemCondition"] = "https://schema.org/NewCondition",
            ["areaServed"] = business.ServiceArea,
            ["seller"] = new Dictionary<string, object?> { ["@id"] = urls.Home() + "#business" },
            ["priceSpecification"] = new Dictionary<string, object?>
            {
                ["@type"] = "UnitPriceSpecification",
                ["price"] = item.PricePerUnit,
                ["priceCurrency"] = "USD",
                ["unitCode"] = "LBR",
                ["unitText"] = item.Unit,
                ["referenceQuantity"] = new Dictionary<string, object?>
                {
                    ["@type"] = "QuantitativeValue",
                    ["value"] = 1,
                    ["unitCode"] = "LBR",
                },
            },
        },
    };

    public static Dictionary<string, object?> Menu(IEnumerable<MenuItemDto> items, BusinessOptions business, IAppUrls urls) => new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "Menu",
        ["name"] = $"{business.Name} menu",
        ["url"] = urls.Menu(),
        ["inLanguage"] = "en-US",
        ["hasMenuSection"] = items
            .GroupBy(i => i.Category)
            .OrderBy(g => g.Key)
            .Select(g => new Dictionary<string, object?>
            {
                ["@type"] = "MenuSection",
                ["name"] = g.First().CategoryName,
                ["hasMenuItem"] = g.Select(i => new Dictionary<string, object?>
                {
                    ["@type"] = "MenuItem",
                    ["name"] = i.Name,
                    ["description"] = i.ShortDescription,
                    ["url"] = urls.MenuItem(i.Slug),
                    ["image"] = string.IsNullOrWhiteSpace(i.ImageUrl) ? null : urls.Absolute(i.ImageUrl),
                    ["offers"] = new Dictionary<string, object?>
                    {
                        ["@type"] = "Offer",
                        ["price"] = i.PricePerUnit,
                        ["priceCurrency"] = "USD",
                        ["eligibleQuantity"] = new Dictionary<string, object?> { ["@type"] = "QuantitativeValue", ["unitCode"] = "LBR", ["value"] = 1 },
                    },
                }).ToArray(),
            })
            .ToArray(),
    };

    public static Dictionary<string, object?> Breadcrumbs(IAppUrls urls, params (string Name, string Path)[] crumbs) => new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "BreadcrumbList",
        ["itemListElement"] = crumbs.Select((c, i) => new Dictionary<string, object?>
        {
            ["@type"] = "ListItem",
            ["position"] = i + 1,
            ["name"] = c.Name,
            ["item"] = urls.Absolute(c.Path),
        }).ToArray(),
    };

    public static Dictionary<string, object?> Faq(IEnumerable<(string Question, string Answer)> entries) => new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "FAQPage",
        ["mainEntity"] = entries.Select(e => new Dictionary<string, object?>
        {
            ["@type"] = "Question",
            ["name"] = e.Question,
            ["acceptedAnswer"] = new Dictionary<string, object?> { ["@type"] = "Answer", ["text"] = e.Answer },
        }).ToArray(),
    };
}
