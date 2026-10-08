namespace ShutkiVorta.Application.Common.Options;

/// <summary>Public business details shown on the website, in structured data and in emails.</summary>
public sealed class BusinessOptions
{
    public const string SectionName = "Business";

    public string Name { get; set; } = "Shutki Vorta Catering";
    public string BengaliName { get; set; } = "শুঁটকি ভর্তা ক্যাটারিং";
    public string Tagline { get; set; } = "Authentic Bangladeshi shutki & vorta, handmade in Dallas";
    public string Phone { get; set; } = "(214) 555-0123";
    public string Email { get; set; } = "hello@example.com";
    public string? StreetAddress { get; set; }
    public string City { get; set; } = "Dallas";
    public string State { get; set; } = "TX";
    public string PostalCode { get; set; } = "75201";
    public string Country { get; set; } = "US";
    public string ServiceArea { get; set; } = "Dallas–Fort Worth";
    public string OpeningHoursText { get; set; } = "Tuesday – Sunday, 11:00 AM – 8:00 PM";
    public string PickupInstructions { get; set; } =
        "Our exact pickup address in Dallas is shared in your order confirmation email.";
    public string TimeZoneId { get; set; } = "America/Chicago";
    public string PriceRange { get; set; } = "$$";
    public double? Latitude { get; set; } = 32.7767;
    public double? Longitude { get; set; } = -96.7970;
    public string? FacebookUrl { get; set; }
    public string? InstagramUrl { get; set; }

    public string CityStateZip => $"{City}, {State} {PostalCode}".Trim();

    public string FullAddress =>
        string.IsNullOrWhiteSpace(StreetAddress) ? CityStateZip : $"{StreetAddress}, {CityStateZip}";
}
