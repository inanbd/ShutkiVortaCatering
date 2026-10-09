namespace ShutkiVorta.Application.Common.Options;

/// <summary>Website-level settings used for absolute URLs and search engine optimisation.</summary>
public sealed class SiteOptions
{
    public const string SectionName = "Site";

    /// <summary>Public root URL without a trailing slash, e.g. "https://www.shutkivorta.com".</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>When false, robots.txt blocks all crawlers and pages emit noindex (use for staging).</summary>
    public bool AllowSearchEngineIndexing { get; set; } = true;

    public string DefaultMetaDescription { get; set; } =
        "Order authentic Bangladeshi shutki vorta and homestyle vortas by the pound in Dallas, TX. Fresh, handmade, available for pickup or local delivery.";

    public string DefaultSocialImage { get; set; } = "/images/og-default.jpg";
    public string? GoogleSiteVerification { get; set; }
    public string? BingSiteVerification { get; set; }

    /// <summary>Extra paths to block in robots.txt, in addition to the built-in private areas.</summary>
    public List<string> AdditionalDisallowedPaths { get; set; } = [];

    public string NormalizedBaseUrl => BaseUrl.TrimEnd('/');
}
