using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ShutkiVorta.Web.Seo;

/// <summary>Per-page search engine and social sharing metadata, rendered by Shared/_SeoHead.cshtml.</summary>
public sealed class SeoMetadata
{
    public required string Title { get; init; }
    public string? Description { get; init; }

    /// <summary>Path of the canonical URL, e.g. "/menu/aloo-vorta". Defaults to the current path.</summary>
    public string? CanonicalPath { get; init; }

    public string? ImageUrl { get; init; }
    public string OpenGraphType { get; init; } = "website";
    public bool NoIndex { get; init; }

    /// <summary>When true the title is used as-is (no " | Business name" suffix).</summary>
    public bool ExactTitle { get; init; }

    public List<object> StructuredData { get; } = [];
}

public static class SeoViewDataExtensions
{
    private const string Key = "Seo";

    public static SeoMetadata SetSeo(this ViewDataDictionary viewData, SeoMetadata seo)
    {
        viewData[Key] = seo;
        viewData["Title"] = seo.Title;
        return seo;
    }

    public static SeoMetadata? GetSeo(this ViewDataDictionary viewData) => viewData[Key] as SeoMetadata;
}
