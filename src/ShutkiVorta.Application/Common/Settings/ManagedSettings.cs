using ShutkiVorta.Application.Common.Options;

namespace ShutkiVorta.Application.Common.Settings;

/// <summary>A configuration section whose values are stored in the database and edited in Admin → Settings.</summary>
/// <param name="Name">Configuration section name, e.g. "Ordering".</param>
/// <param name="OptionsType">The options class bound from the section.</param>
/// <param name="CreateDefaults">Values for a brand-new installation (lists included, which the class initialisers leave empty).</param>
public sealed record ManagedSection(string Name, Type OptionsType, Func<object> CreateDefaults);

/// <summary>
/// The settings that live in the database instead of appsettings.json. Infrastructure settings that are needed before the
/// database can be reached (connection strings, data-protection keys, logging, the first admin account) stay in appsettings.json.
/// </summary>
public static class ManagedSettings
{
    public const string SmtpPasswordKey = "Email:Smtp:Password";

    public static readonly IReadOnlyList<ManagedSection> Sections =
    [
        new(BusinessOptions.SectionName, typeof(BusinessOptions), () => new BusinessOptions()),
        new(OrderingOptions.SectionName, typeof(OrderingOptions), () => new OrderingOptions
        {
            ClosedDays = [DayOfWeek.Monday],
            DeliveryZipPrefixes = ["750", "751", "752", "753", "760", "761"],
        }),
        new(WholesaleOptions.SectionName, typeof(WholesaleOptions), () => new WholesaleOptions()),
        new(EmailOptions.SectionName, typeof(EmailOptions), () => new EmailOptions()),
        new(SiteOptions.SectionName, typeof(SiteOptions), () => new SiteOptions()),
        new(AccountOptions.SectionName, typeof(AccountOptions), () => new AccountOptions()),
        new(RateLimitingOptions.SectionName, typeof(RateLimitingOptions), () => new RateLimitingOptions()),
    ];

    /// <summary>Settings stored encrypted and never shown back in the admin UI.</summary>
    public static readonly IReadOnlySet<string> SecretKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SmtpPasswordKey };

    public static ManagedSection? Find(string? section) =>
        Sections.FirstOrDefault(s => string.Equals(s.Name, section, StringComparison.OrdinalIgnoreCase));

    public static bool IsManagedKey(string key) =>
        Sections.Any(s => key.StartsWith(s.Name + ":", StringComparison.OrdinalIgnoreCase));

    public static bool IsSecret(string key) => SecretKeys.Contains(key);
}
