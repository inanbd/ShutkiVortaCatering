using System.Globalization;
using FluentValidation;
using ShutkiVorta.Application.Common.Options;

namespace ShutkiVorta.Application.Features.Settings;

/// <summary>Shared rules for values edited in Admin → Settings.</summary>
internal static class SettingRules
{
    public static bool IsTime(string? value) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static TimeOnly ParseTime(string? value) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : TimeOnly.MinValue;

    public static bool IsDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    public static bool IsEmptyOrHttpUrl(string? value) => string.IsNullOrWhiteSpace(value) || IsHttpUrl(value);

    public static bool IsTimeZone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(value);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    public static IRuleBuilderOptions<T, string> ValidTime<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Must(IsTime).WithMessage("Please use the 24-hour format HH:mm, e.g. 09:30 or 17:00.");
}

internal sealed class BusinessOptionsValidator : AbstractValidator<BusinessOptions>
{
    public BusinessOptionsValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.BengaliName).MaximumLength(120);
        RuleFor(x => x.Tagline).MaximumLength(200);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(32)
            .Must(p => p is not null && p.Count(char.IsDigit) >= 7).WithMessage("Please enter a phone number customers can call.");
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.StreetAddress).MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.State).NotEmpty().MaximumLength(50);
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Country).NotEmpty().Length(2).WithMessage("Use the 2-letter country code, e.g. US.");
        RuleFor(x => x.ServiceArea).MaximumLength(120);
        RuleFor(x => x.OpeningHoursText).MaximumLength(200);
        RuleFor(x => x.PickupInstructions).MaximumLength(500);
        RuleFor(x => x.TimeZoneId).Must(SettingRules.IsTimeZone).WithMessage("Please choose a time zone from the list.");
        RuleFor(x => x.PriceRange).Must(p => p is "$" or "$$" or "$$$" or "$$$$").WithMessage("Choose $, $$, $$$ or $$$$.");
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude.HasValue);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude.HasValue);
        RuleFor(x => x.FacebookUrl).Must(SettingRules.IsEmptyOrHttpUrl).WithMessage("Please enter a full web address starting with https://");
        RuleFor(x => x.InstagramUrl).Must(SettingRules.IsEmptyOrHttpUrl).WithMessage("Please enter a full web address starting with https://");
    }
}

internal sealed class OrderingOptionsValidator : AbstractValidator<OrderingOptions>
{
    public OrderingOptionsValidator()
    {
        RuleFor(x => x.PausedMessage).NotEmpty().MaximumLength(500);
        RuleFor(x => x.MinimumLeadTimeHours).InclusiveBetween(0, 720);
        RuleFor(x => x.MaxDaysInAdvance).InclusiveBetween(1, 365);
        RuleFor(x => x.FirstSlot).ValidTime();
        RuleFor(x => x.LastSlot).ValidTime();
        RuleFor(x => x.LastSlot)
            .Must((o, last) => SettingRules.ParseTime(last) >= SettingRules.ParseTime(o.FirstSlot))
            .When(o => SettingRules.IsTime(o.FirstSlot) && SettingRules.IsTime(o.LastSlot))
            .WithMessage("The last time must be the same as or later than the first time.");
        RuleFor(x => x.SlotIntervalMinutes).InclusiveBetween(5, 240);
        RuleFor(x => x.ClosedDays).Must(d => d.Distinct().Count() < 7).WithMessage("The kitchen must be open on at least one day.");
        RuleForEach(x => x.BlackoutDates).Must(SettingRules.IsDate).WithMessage("'{PropertyValue}' is not a date. Use yyyy-mm-dd, e.g. 2027-03-20.");
        RuleFor(x => x.DeliveryFee).InclusiveBetween(0, 500);
        RuleFor(x => x.FreeDeliveryThreshold).InclusiveBetween(0, 10000).When(x => x.FreeDeliveryThreshold.HasValue);
        RuleFor(x => x.MinimumDeliverySubtotal).InclusiveBetween(0, 10000);
        RuleFor(x => x.TaxRate).InclusiveBetween(0, 0.25m).WithMessage("Sales tax must be between 0% and 25%.");
        RuleForEach(x => x.DeliveryZipPrefixes)
            .Must(z => z is { Length: >= 1 and <= 5 } && z.All(char.IsDigit))
            .WithMessage("'{PropertyValue}' is not a ZIP code or ZIP prefix (1–5 digits).");
        RuleFor(x => x.DeliveryAreaDescription).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PaymentInstructions).NotEmpty().MaximumLength(500);
    }
}

internal sealed class WholesaleOptionsValidator : AbstractValidator<WholesaleOptions>
{
    public WholesaleOptionsValidator()
    {
        RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 90);
        RuleFor(x => x.MinimumQuantityPerItem).GreaterThan(0).LessThanOrEqualTo(1000);
        RuleFor(x => x.QuantityStep).InclusiveBetween(0, 100);
        RuleFor(x => x.MinimumSubtotalPerDelivery).InclusiveBetween(0, 100000);
        RuleFor(x => x.DeliveryFee).InclusiveBetween(0, 500);
        RuleFor(x => x.LeadTimeDays).InclusiveBetween(0, 60);
        RuleFor(x => x.GenerateDaysAhead).InclusiveBetween(1, 60);
        RuleFor(x => x.ChangeCutoffHours).InclusiveBetween(0, 168);
        RuleFor(x => x.FirstSlot).ValidTime();
        RuleFor(x => x.LastSlot).ValidTime();
        RuleFor(x => x.LastSlot)
            .Must((o, last) => SettingRules.ParseTime(last) >= SettingRules.ParseTime(o.FirstSlot))
            .When(o => SettingRules.IsTime(o.FirstSlot) && SettingRules.IsTime(o.LastSlot))
            .WithMessage("The last time must be the same as or later than the first time.");
        RuleFor(x => x.SlotIntervalMinutes).InclusiveBetween(5, 240);
        RuleFor(x => x.DeliveryAreaDescription).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PaymentTerms).NotEmpty().MaximumLength(500);
    }
}

internal sealed class EmailOptionsValidator : AbstractValidator<EmailOptions>
{
    private static readonly string[] Methods = [EmailDeliveryMethods.Auto, EmailDeliveryMethods.Smtp, EmailDeliveryMethods.PickupDirectory];
    private static readonly string[] SecurityModes = ["Auto", "StartTls", "SslOnConnect", "None"];

    public EmailOptionsValidator()
    {
        RuleFor(x => x.DeliveryMethod).Must(m => Methods.Contains(m, StringComparer.OrdinalIgnoreCase)).WithMessage("Choose a delivery method from the list.");
        RuleFor(x => x.PickupDirectory).NotEmpty().MaximumLength(260);
        RuleFor(x => x.FromName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.FromAddress).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.ReplyToAddress).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.ReplyToAddress));
        RuleFor(x => x.AdminRecipients).Must(r => r.Count <= 20).WithMessage("Please list at most 20 addresses.");
        RuleForEach(x => x.AdminRecipients).EmailAddress().WithMessage("'{PropertyValue}' is not an email address.");
        RuleFor(x => x.Smtp.Host).MaximumLength(255)
            .Must(h => string.IsNullOrEmpty(h) || (!h.Contains("://") && !h.Any(char.IsWhiteSpace) && !h.Contains(':')))
            .WithMessage("Enter only the server name, e.g. smtp.gmail.com (no https:// and no port).");
        RuleFor(x => x.Smtp.Port).InclusiveBetween(1, 65535);
        RuleFor(x => x.Smtp.Security).Must(s => SecurityModes.Contains(s, StringComparer.OrdinalIgnoreCase)).WithMessage("Choose an encryption option from the list.");
        RuleFor(x => x.Smtp.UserName).MaximumLength(256);
        RuleFor(x => x.Smtp.TimeoutSeconds).InclusiveBetween(5, 300);
        RuleFor(x => x.Smtp.LocalDomain).MaximumLength(255);
    }
}

internal sealed class SiteOptionsValidator : AbstractValidator<SiteOptions>
{
    public SiteOptionsValidator()
    {
        RuleFor(x => x.BaseUrl)
            .Must(u => string.IsNullOrWhiteSpace(u) || (SettingRules.IsHttpUrl(u) && new Uri(u).AbsolutePath == "/" && string.IsNullOrEmpty(new Uri(u).Query)))
            .WithMessage("Enter just your web address, e.g. https://www.yourdomain.com (no page path).");
        RuleFor(x => x.DefaultMetaDescription).NotEmpty().MaximumLength(300);
        RuleFor(x => x.DefaultSocialImage).NotEmpty().MaximumLength(500)
            .Must(i => i.StartsWith('/') || SettingRules.IsHttpUrl(i)).WithMessage("Use a site path such as /images/photo.jpg or a full https:// address.");
        RuleFor(x => x.GoogleSiteVerification).MaximumLength(200);
        RuleFor(x => x.BingSiteVerification).MaximumLength(200);
        RuleForEach(x => x.AdditionalDisallowedPaths).Must(p => p.StartsWith('/') && !p.Any(char.IsWhiteSpace))
            .WithMessage("'{PropertyValue}' must be a path starting with /, e.g. /drafts.");
    }
}

internal sealed class RateLimitingOptionsValidator : AbstractValidator<RateLimitingOptions>
{
    public RateLimitingOptionsValidator()
    {
        RuleFor(x => x.FormPostsPerWindow).InclusiveBetween(1, 1000);
        RuleFor(x => x.WindowMinutes).InclusiveBetween(1, 1440);
    }
}

internal sealed class AccountOptionsValidator : AbstractValidator<AccountOptions>;
