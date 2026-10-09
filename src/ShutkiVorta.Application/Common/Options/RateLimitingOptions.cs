namespace ShutkiVorta.Application.Common.Options;

/// <summary>How often one visitor (IP address) may submit each public form that sends email or creates an account.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int FormPostsPerWindow { get; set; } = 10;

    public int WindowMinutes { get; set; } = 10;
}
