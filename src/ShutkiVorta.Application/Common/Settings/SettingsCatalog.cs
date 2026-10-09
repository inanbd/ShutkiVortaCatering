using System.Globalization;
using ShutkiVorta.Application.Common.Options;

namespace ShutkiVorta.Application.Common.Settings;

public enum SettingKind
{
    Text,
    Multiline,
    Email,
    Url,
    Phone,
    Integer,
    Decimal,
    Money,

    /// <summary>Stored as a fraction (0.0825), edited as a percentage (8.25).</summary>
    Percent,

    Bool,

    /// <summary>24-hour "HH:mm".</summary>
    Time,

    Select,
    DaysOfWeek,

    /// <summary>A list edited one value per line.</summary>
    List,

    /// <summary>Stored encrypted; never shown again after saving.</summary>
    Secret,

    TimeZone,
}

public sealed record SettingChoice(string Value, string Label);

/// <summary>One editable setting. <see cref="Key"/> is the full configuration path, e.g. "Email:Smtp:Port".</summary>
public sealed record SettingField(string Key, string Label, SettingKind Kind)
{
    public string? Help { get; init; }
    public string? Placeholder { get; init; }
    public string Group { get; init; } = string.Empty;
    public decimal? Min { get; init; }
    public decimal? Max { get; init; }
    public decimal? Step { get; init; }
    public IReadOnlyList<SettingChoice> Choices { get; init; } = [];

    /// <summary>Rarely needed; shown collapsed under "Advanced".</summary>
    public bool Advanced { get; init; }

    public string Name => Key[(Key.LastIndexOf(':') + 1)..];
}

/// <summary>A page of settings in the admin UI; maps 1:1 to a configuration section.</summary>
public sealed record SettingsPage(string Slug, string Section, string Title, string Description, string Icon, IReadOnlyList<SettingField> Fields);

/// <summary>Labels, help text and input types for every setting stored in the database.</summary>
public static class SettingsCatalog
{
    private static readonly IReadOnlyList<SettingChoice> Weekdays =
        [.. new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }
            .Select(d => new SettingChoice(d.ToString(), d.ToString()))];

    public static readonly IReadOnlyList<SettingsPage> Pages =
    [
        new("business", BusinessOptions.SectionName, "Business details", "Your name, contact details and address as shown on the website, in emails and to search engines.", "dashboard",
        [
            Field("Business:Name", "Business name", SettingKind.Text, "Identity") with { Placeholder = "Shutki Vorta Catering" },
            Field("Business:BengaliName", "Name in Bengali", SettingKind.Text, "Identity"),
            Field("Business:Tagline", "Tagline", SettingKind.Text, "Identity") with { Help = "A short line used in emails and page titles." },
            Field("Business:Phone", "Phone", SettingKind.Phone, "Contact"),
            Field("Business:Email", "Public email", SettingKind.Email, "Contact") with { Help = "Shown on the website. Notification emails are configured under Email." },
            Field("Business:OpeningHoursText", "Opening hours", SettingKind.Text, "Contact") with { Placeholder = "Tuesday – Sunday, 11:00 AM – 8:00 PM" },
            Field("Business:StreetAddress", "Street address", SettingKind.Text, "Address") with { Help = "Leave empty to keep your street address private; it is then only shared in order confirmations." },
            Field("Business:City", "City", SettingKind.Text, "Address"),
            Field("Business:State", "State", SettingKind.Text, "Address") with { Placeholder = "TX" },
            Field("Business:PostalCode", "ZIP code", SettingKind.Text, "Address"),
            Field("Business:Country", "Country code", SettingKind.Text, "Address") with { Placeholder = "US", Advanced = true },
            Field("Business:ServiceArea", "Service area", SettingKind.Text, "Address") with { Placeholder = "Dallas–Fort Worth" },
            Field("Business:PickupInstructions", "Pickup instructions", SettingKind.Multiline, "Address"),
            Field("Business:TimeZoneId", "Time zone", SettingKind.TimeZone, "Address") with { Help = "All order times, slots and deliveries use this time zone." },
            Field("Business:Latitude", "Latitude", SettingKind.Decimal, "Search engines") with { Min = -90, Max = 90, Step = 0.0001m, Help = "Optional. Helps Google show you on maps.", Advanced = true },
            Field("Business:Longitude", "Longitude", SettingKind.Decimal, "Search engines") with { Min = -180, Max = 180, Step = 0.0001m, Advanced = true },
            Field("Business:PriceRange", "Price range", SettingKind.Select, "Search engines") with { Choices = [new("$", "$"), new("$$", "$$"), new("$$$", "$$$"), new("$$$$", "$$$$")], Advanced = true },
            Field("Business:FacebookUrl", "Facebook page", SettingKind.Url, "Social") with { Placeholder = "https://www.facebook.com/yourpage" },
            Field("Business:InstagramUrl", "Instagram", SettingKind.Url, "Social") with { Placeholder = "https://www.instagram.com/yourname" },
        ]),

        new("ordering", OrderingOptions.SectionName, "Online orders", "When customers can order from the website, delivery rules, fees and sales tax.", "receipt",
        [
            Field("Ordering:AcceptingOrders", "Accept online orders", SettingKind.Bool, "Accepting orders") with { Help = "Turn off to pause checkout (for holidays or when the kitchen is full)." },
            Field("Ordering:PausedMessage", "Message while paused", SettingKind.Multiline, "Accepting orders"),
            Field("Ordering:MinimumLeadTimeHours", "Notice needed (hours)", SettingKind.Integer, "Schedule") with { Min = 0, Max = 720, Help = "How far ahead an order must be placed." },
            Field("Ordering:MaxDaysInAdvance", "Book up to (days ahead)", SettingKind.Integer, "Schedule") with { Min = 1, Max = 365 },
            Field("Ordering:FirstSlot", "First pickup/delivery time", SettingKind.Time, "Schedule"),
            Field("Ordering:LastSlot", "Last pickup/delivery time", SettingKind.Time, "Schedule"),
            Field("Ordering:SlotIntervalMinutes", "Minutes between time slots", SettingKind.Integer, "Schedule") with { Min = 15, Max = 240, Step = 5 },
            Field("Ordering:ClosedDays", "Kitchen closed on", SettingKind.DaysOfWeek, "Schedule") with { Choices = Weekdays, Help = "Applies to online and restaurant orders." },
            Field("Ordering:BlackoutDates", "Holiday closures", SettingKind.List, "Schedule") with { Placeholder = "2027-03-20", Help = "One date per line (yyyy-mm-dd), e.g. Eid. Applies to online and restaurant orders." },
            Field("Ordering:DeliveryFee", "Delivery fee", SettingKind.Money, "Delivery") with { Min = 0, Max = 500 },
            Field("Ordering:FreeDeliveryThreshold", "Free delivery from", SettingKind.Money, "Delivery") with { Min = 0, Max = 10000, Help = "Order subtotal for free delivery. Leave empty for no free delivery." },
            Field("Ordering:MinimumDeliverySubtotal", "Minimum order for delivery", SettingKind.Money, "Delivery") with { Min = 0, Max = 10000 },
            Field("Ordering:DeliveryZipPrefixes", "Delivery ZIP codes", SettingKind.List, "Delivery") with { Placeholder = "752", Help = "One ZIP code or ZIP prefix per line (752 = all of 752xx). Leave empty to deliver anywhere." },
            Field("Ordering:DeliveryAreaDescription", "Delivery area (shown to customers)", SettingKind.Text, "Delivery"),
            Field("Ordering:TaxRate", "Sales tax", SettingKind.Percent, "Tax & payment") with { Min = 0, Max = 25, Step = 0.001m, Help = "Dallas: 8.25%." },
            Field("Ordering:TaxDeliveryFee", "Charge sales tax on the delivery fee", SettingKind.Bool, "Tax & payment"),
            Field("Ordering:PaymentInstructions", "Payment instructions", SettingKind.Multiline, "Tax & payment"),
        ]),

        new("restaurants", WholesaleOptions.SectionName, "Restaurant orders", "Wholesale prices and rules for restaurant standing orders.", "calendar",
        [
            Field("Wholesale:AcceptingRequests", "Accept new restaurant requests", SettingKind.Bool, "Requests"),
            Field("Wholesale:DiscountPercent", "Default wholesale discount (%)", SettingKind.Decimal, "Pricing") with { Min = 0, Max = 90, Step = 0.5m, Help = "Used for items without their own wholesale price." },
            Field("Wholesale:MinimumQuantityPerItem", "Minimum per vorta (lb)", SettingKind.Decimal, "Pricing") with { Min = 0.5m, Max = 1000, Step = 0.5m },
            Field("Wholesale:QuantityStep", "Order in steps of (lb)", SettingKind.Decimal, "Pricing") with { Min = 0, Max = 100, Step = 0.5m, Help = "0 allows any amount above the minimum." },
            Field("Wholesale:MinimumSubtotalPerDelivery", "Minimum per delivery", SettingKind.Money, "Pricing") with { Min = 0, Max = 100000 },
            Field("Wholesale:DeliveryFee", "Delivery fee per delivery", SettingKind.Money, "Pricing") with { Min = 0, Max = 500 },
            Field("Wholesale:PaymentTerms", "Payment terms", SettingKind.Multiline, "Pricing"),
            Field("Wholesale:LeadTimeDays", "Days before the first delivery", SettingKind.Integer, "Schedule") with { Min = 0, Max = 60 },
            Field("Wholesale:ChangeCutoffHours", "Changes allowed until (hours before)", SettingKind.Integer, "Schedule") with { Min = 0, Max = 168, Help = "Restaurants can skip or pause deliveries online until this many hours before." },
            Field("Wholesale:FirstSlot", "First delivery time", SettingKind.Time, "Schedule"),
            Field("Wholesale:LastSlot", "Last delivery time", SettingKind.Time, "Schedule"),
            Field("Wholesale:SlotIntervalMinutes", "Minutes between delivery times", SettingKind.Integer, "Schedule") with { Min = 15, Max = 240, Step = 5 },
            Field("Wholesale:DeliveryAreaDescription", "Delivery area (shown to restaurants)", SettingKind.Text, "Schedule"),
            Field("Wholesale:AutoGenerate", "Create upcoming orders automatically", SettingKind.Bool, "Order generation") with { Help = "Runs every hour. When off, use \"Generate upcoming deliveries now\" on the Restaurant orders page." },
            Field("Wholesale:GenerateDaysAhead", "Create orders this many days ahead", SettingKind.Integer, "Order generation") with { Min = 1, Max = 60 },
        ]),

        new("email", EmailOptions.SectionName, "Email", "Your mail server and who receives notifications. Use \"Test connection\" after saving.", "mail",
        [
            Field("Email:Enabled", "Send emails", SettingKind.Bool, "Notifications") with { Help = "Master switch. While off, emails are recorded in the email log but never sent (not even later)." },
            Field("Email:AdminRecipients", "Notify about new orders", SettingKind.List, "Notifications") with { Placeholder = "owner@yourdomain.com", Help = "One email address per line. They receive new orders, inquiries and restaurant requests." },
            Field("Email:SendCustomerStatusUpdates", "Email customers when their order status changes", SettingKind.Bool, "Notifications"),
            Field("Email:FromName", "Sender name", SettingKind.Text, "Sender"),
            Field("Email:FromAddress", "Sender address", SettingKind.Email, "Sender") with { Help = "Must be an address your mail account is allowed to send from." },
            Field("Email:ReplyToAddress", "Reply-to address", SettingKind.Email, "Sender") with { Help = "Optional. Where customer replies go." },
            Field("Email:Smtp:Host", "SMTP server", SettingKind.Text, "Mail server (SMTP)") with { Placeholder = "smtp.yourprovider.com", Help = "Leave empty to only save emails to a folder (testing)." },
            Field("Email:Smtp:Port", "Port", SettingKind.Integer, "Mail server (SMTP)") with { Min = 1, Max = 65535, Help = "587 (STARTTLS) or 465 (SSL/TLS)." },
            Field("Email:Smtp:Security", "Encryption", SettingKind.Select, "Mail server (SMTP)") with
            {
                Choices = [new("Auto", "Automatic (recommended)"), new("StartTls", "STARTTLS"), new("SslOnConnect", "SSL/TLS"), new("None", "None (only for a server on this machine)")],
            },
            Field("Email:Smtp:UserName", "User name", SettingKind.Text, "Mail server (SMTP)"),
            Field(ManagedSettings.SmtpPasswordKey, "Password", SettingKind.Secret, "Mail server (SMTP)") with { Help = "Stored encrypted. Gmail and Outlook need an app password." },
            Field("Email:Smtp:TimeoutSeconds", "Timeout (seconds)", SettingKind.Integer, "Mail server (SMTP)") with { Min = 5, Max = 300, Advanced = true },
            Field("Email:Smtp:AcceptInvalidCertificates", "Accept untrusted certificates", SettingKind.Bool, "Mail server (SMTP)") with { Help = "Only for your own mail server with a self-signed certificate.", Advanced = true },
            Field("Email:Smtp:CheckCertificateRevocation", "Check certificate revocation", SettingKind.Bool, "Mail server (SMTP)") with { Advanced = true },
            Field("Email:Smtp:LocalDomain", "HELO name", SettingKind.Text, "Mail server (SMTP)") with { Help = "Only if your mail server rejects the default.", Advanced = true },
            Field("Email:DeliveryMethod", "Delivery method", SettingKind.Select, "Mail server (SMTP)") with
            {
                Choices = [new("Auto", "Automatic: send when an SMTP server is set"), new("Smtp", "Always send through SMTP"), new("PickupDirectory", "Never send; save to a folder (testing)")],
                Advanced = true,
            },
        ]),

        new("website", SiteOptions.SectionName, "Website & search engines", "Your web address and how search engines see the site.", "search",
        [
            Field("Site:BaseUrl", "Website address", SettingKind.Url, "Website") with { Placeholder = "https://www.yourdomain.com", Help = "Used in every email link (including password resets), the sitemap and social media. Set it to your real address once the site is live." },
            Field("Site:AllowSearchEngineIndexing", "Let search engines list the site", SettingKind.Bool, "Search engines") with { Help = "Turn off for a test copy of the site." },
            Field("Site:DefaultMetaDescription", "Default description for search results", SettingKind.Multiline, "Search engines"),
            Field("Site:DefaultSocialImage", "Default image for social media", SettingKind.Text, "Search engines") with { Placeholder = "/images/og-default.jpg" },
            Field("Site:GoogleSiteVerification", "Google Search Console code", SettingKind.Text, "Search engines") with { Advanced = true },
            Field("Site:BingSiteVerification", "Bing Webmaster code", SettingKind.Text, "Search engines") with { Advanced = true },
            Field("Site:AdditionalDisallowedPaths", "Extra paths to hide from search engines", SettingKind.List, "Search engines") with { Placeholder = "/drafts", Advanced = true },
        ]),

        new("accounts", AccountOptions.SectionName, "Customer accounts", "Rules for customer sign-up and sign-in.", "users",
        [
            Field("Identity:RequireConfirmedEmail", "Require email confirmation before sign-in", SettingKind.Bool, "Sign-in") with { Help = "Customers must click the link in their welcome email before they can sign in. Administrators are never blocked." },
        ]),

        new("spam-protection", RateLimitingOptions.SectionName, "Spam protection", "How often one visitor can send the contact, catering, kitchen, restaurant and sign-up forms.", "check-circle",
        [
            Field("RateLimiting:FormPostsPerWindow", "Submissions allowed per form", SettingKind.Integer, "Limits") with { Min = 3, Max = 1000, Help = "Per visitor. If the site runs behind a proxy, make sure forwarded headers are enabled so visitors are told apart." },
            Field("RateLimiting:WindowMinutes", "…within this many minutes", SettingKind.Integer, "Limits") with { Min = 1, Max = 1440 },
        ]),
    ];

    public static SettingsPage? FindBySlug(string? slug) =>
        Pages.FirstOrDefault(p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public static SettingsPage? FindBySection(string? section) =>
        Pages.FirstOrDefault(p => string.Equals(p.Section, section, StringComparison.OrdinalIgnoreCase));

    /// <summary>Time zones for the time-zone picker (IANA ids, US zones first).</summary>
    public static IReadOnlyList<SettingChoice> TimeZones { get; } = BuildTimeZones();

    private static SettingField Field(string key, string label, SettingKind kind, string group) => new(key, label, kind) { Group = group };

    private static IReadOnlyList<SettingChoice> BuildTimeZones()
    {
        string[] us = ["America/Chicago", "America/New_York", "America/Denver", "America/Phoenix", "America/Los_Angeles", "America/Anchorage", "Pacific/Honolulu"];
        var all = TimeZoneInfo.GetSystemTimeZones()
            .Select(z => TimeZoneInfo.TryConvertWindowsIdToIanaId(z.Id, out var iana) ? iana : z.Id)
            .Where(id => id.Contains('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);
        return [.. us.Concat(all.Except(us, StringComparer.OrdinalIgnoreCase)).Select(id => new SettingChoice(id, Describe(id)))];
    }

    private static string Describe(string id)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            var offset = zone.GetUtcOffset(DateTime.UtcNow);
            return $"{id.Replace('_', ' ')} (UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture)})";
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return id;
        }
    }
}
