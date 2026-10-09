using FluentValidation;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Settings;

namespace ShutkiVorta.UnitTests.Application.Settings;

/// <summary>The rules applied when an admin saves a settings page.</summary>
public sealed class SettingsValidatorsTests
{
    private static List<string> Errors<T>(AbstractValidator<T> validator, T options, string property) =>
        [.. validator.Validate(options).Errors.Where(e => e.PropertyName == property).Select(e => e.ErrorMessage)];

    private static void Valid<T>(AbstractValidator<T> validator, T options)
    {
        var result = validator.Validate(options);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")));
    }

    // ---- Shared rules -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("09:30", true)]
    [InlineData("00:00", true)]
    [InlineData("23:59", true)]
    [InlineData("9:30", false)]
    [InlineData("24:00", false)]
    [InlineData("09:30:00", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsTime_RequiresHHmm(string? value, bool expected) => Assert.Equal(expected, SettingRules.IsTime(value));

    [Theory]
    [InlineData("2027-03-20", true)]
    [InlineData("2027-3-20", false)]
    [InlineData("20/03/2027", false)]
    [InlineData("2027-02-30", false)]
    [InlineData(null, false)]
    public void IsDate_RequiresYyyyMmDd(string? value, bool expected) => Assert.Equal(expected, SettingRules.IsDate(value));

    [Theory]
    [InlineData("America/Chicago", true)]
    [InlineData("Asia/Dhaka", true)]
    [InlineData("UTC", true)]
    [InlineData("Mars/Olympus_Mons", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsTimeZone_AcceptsOnlyKnownZones(string? value, bool expected) => Assert.Equal(expected, SettingRules.IsTimeZone(value));

    [Theory]
    [InlineData("https://www.facebook.com/shutki", true)]
    [InlineData("http://example.com", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("www.facebook.com/shutki", false)]
    [InlineData("javascript:alert(1)", false)]
    public void IsHttpUrl_AcceptsOnlyAbsoluteWebAddresses(string value, bool expected) => Assert.Equal(expected, SettingRules.IsHttpUrl(value));

    [Fact]
    public void ParseTime_FallsBackToMidnightForInvalidValues()
    {
        Assert.Equal(new TimeOnly(17, 30), SettingRules.ParseTime("17:30"));
        Assert.Equal(TimeOnly.MinValue, SettingRules.ParseTime("late"));
    }

    // ---- Business ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Business_Defaults_AreValid() => Valid(new BusinessOptionsValidator(), new BusinessOptions());

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("")]
    [InlineData("Central Standard Time (US)")]
    public void Business_UnknownTimeZone_IsRejected(string zone)
    {
        Assert.Contains("Please choose a time zone from the list.", Errors(new BusinessOptionsValidator(), new BusinessOptions { TimeZoneId = zone }, "TimeZoneId"));
    }

    [Theory]
    [InlineData("555-12")]
    [InlineData("call us")]
    public void Business_PhoneNeedsSevenDigits(string phone)
    {
        Assert.Contains("Please enter a phone number customers can call.", Errors(new BusinessOptionsValidator(), new BusinessOptions { Phone = phone }, "Phone"));
    }

    [Fact]
    public void Business_FieldRules()
    {
        var v = new BusinessOptionsValidator();
        Assert.NotEmpty(Errors(v, new BusinessOptions { Name = "" }, "Name"));
        Assert.NotEmpty(Errors(v, new BusinessOptions { Email = "not-an-email" }, "Email"));
        Assert.Contains("Use the 2-letter country code, e.g. US.", Errors(v, new BusinessOptions { Country = "USA" }, "Country"));
        Assert.Contains("Choose $, $$, $$$ or $$$$.", Errors(v, new BusinessOptions { PriceRange = "$$$$$" }, "PriceRange"));
        Assert.NotEmpty(Errors(v, new BusinessOptions { Latitude = 90.01 }, "Latitude"));
        Assert.NotEmpty(Errors(v, new BusinessOptions { Longitude = -180.5 }, "Longitude"));
        Assert.Empty(Errors(v, new BusinessOptions { Latitude = null, Longitude = null }, "Latitude"));
        Assert.Contains("Please enter a full web address starting with https://", Errors(v, new BusinessOptions { FacebookUrl = "facebook.com/shutki" }, "FacebookUrl"));
        Assert.Empty(Errors(v, new BusinessOptions { FacebookUrl = "", InstagramUrl = "https://instagram.com/shutki" }, "FacebookUrl"));
        Assert.NotEmpty(Errors(v, new BusinessOptions { Tagline = new string('x', 201) }, "Tagline"));
        Assert.Empty(Errors(v, new BusinessOptions { StreetAddress = "" }, "StreetAddress"));
    }

    // ---- Online orders ------------------------------------------------------------------------------------------------

    [Fact]
    public void Ordering_Defaults_AreValid() => Valid(new OrderingOptionsValidator(), new OrderingOptions { ClosedDays = [DayOfWeek.Monday], DeliveryZipPrefixes = ["752"] });

    [Fact]
    public void Ordering_LastSlotBeforeFirstSlot_IsRejectedOnTheLastSlot()
    {
        var v = new OrderingOptionsValidator();
        var options = new OrderingOptions { FirstSlot = "18:00", LastSlot = "09:00" };

        Assert.Contains("The last time must be the same as or later than the first time.", Errors(v, options, "LastSlot"));
        Assert.Empty(Errors(v, options, "FirstSlot"));
        Assert.Empty(Errors(v, new OrderingOptions { FirstSlot = "12:00", LastSlot = "12:00" }, "LastSlot"));
    }

    [Fact]
    public void Ordering_SlotsMustBe24HourTimes()
    {
        var v = new OrderingOptionsValidator();
        Assert.Contains("Please use the 24-hour format HH:mm, e.g. 09:30 or 17:00.", Errors(v, new OrderingOptions { FirstSlot = "9am" }, "FirstSlot"));
        Assert.NotEmpty(Errors(v, new OrderingOptions { LastSlot = "" }, "LastSlot"));

        // The order rule is skipped while a time is invalid, so only the format error is shown.
        Assert.Single(Errors(v, new OrderingOptions { FirstSlot = "11:00", LastSlot = "7pm" }, "LastSlot"));
    }

    [Theory]
    [InlineData("0.2501", false)]
    [InlineData("-0.01", false)]
    [InlineData("0.25", true)]
    [InlineData("0", true)]
    [InlineData("0.0825", true)]
    public void Ordering_TaxRateIsBetween0And25Percent(string rate, bool valid)
    {
        var errors = Errors(new OrderingOptionsValidator(), new OrderingOptions { TaxRate = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture) }, "TaxRate");
        if (valid)
        {
            Assert.Empty(errors);
        }
        else
        {
            Assert.Equal(["Sales tax must be between 0% and 25%."], errors); // and nothing about the rate looking too low
        }
    }

    [Fact]
    public void Ordering_KitchenMustBeOpenOnAtLeastOneDay()
    {
        var v = new OrderingOptionsValidator();
        Assert.Contains("The kitchen must be open on at least one day.", Errors(v, new OrderingOptions { ClosedDays = [.. Enum.GetValues<DayOfWeek>()] }, "ClosedDays"));
        Assert.Empty(Errors(v, new OrderingOptions { ClosedDays = [.. Enum.GetValues<DayOfWeek>().Skip(1)] }, "ClosedDays"));
        Assert.Empty(Errors(v, new OrderingOptions { ClosedDays = [DayOfWeek.Monday, DayOfWeek.Monday] }, "ClosedDays"));
        Assert.Empty(Errors(v, new OrderingOptions { ClosedDays = [] }, "ClosedDays"));
    }

    [Theory]
    [InlineData("7")]
    [InlineData("752")]
    [InlineData("75201")]
    public void Ordering_ZipPrefixes_AcceptOneToFiveDigits(string zip)
    {
        Assert.True(new OrderingOptionsValidator().Validate(new OrderingOptions { DeliveryZipPrefixes = [zip] }).IsValid);
    }

    [Theory]
    [InlineData("752011")]
    [InlineData("75A")]
    [InlineData("752 01")]
    [InlineData("")]
    [InlineData("-752")]
    public void Ordering_ZipPrefixes_RejectAnythingElse_NamingTheValue(string zip)
    {
        var errors = new OrderingOptionsValidator().Validate(new OrderingOptions { DeliveryZipPrefixes = ["752", zip] }).Errors;

        var error = Assert.Single(errors);
        Assert.Equal("DeliveryZipPrefixes[1]", error.PropertyName);
        Assert.Equal($"'{zip}' is not a ZIP code or ZIP prefix (1–5 digits).", error.ErrorMessage);
    }

    [Fact]
    public void Ordering_BlackoutDates_MustBeIsoDates()
    {
        var errors = new OrderingOptionsValidator().Validate(new OrderingOptions { BlackoutDates = ["2027-03-20", "20/03/2027"] }).Errors;

        var error = Assert.Single(errors);
        Assert.Equal("BlackoutDates[1]", error.PropertyName);
        Assert.Equal("'20/03/2027' is not a date. Use yyyy-mm-dd, e.g. 2027-03-20.", error.ErrorMessage);
    }

    [Fact]
    public void Ordering_NumberRanges()
    {
        var v = new OrderingOptionsValidator();
        Assert.NotEmpty(Errors(v, new OrderingOptions { MinimumLeadTimeHours = 721 }, "MinimumLeadTimeHours"));
        Assert.NotEmpty(Errors(v, new OrderingOptions { MaxDaysInAdvance = 0 }, "MaxDaysInAdvance"));
        Assert.NotEmpty(Errors(v, new OrderingOptions { SlotIntervalMinutes = 4 }, "SlotIntervalMinutes"));
        Assert.NotEmpty(Errors(v, new OrderingOptions { DeliveryFee = -1 }, "DeliveryFee"));
        Assert.NotEmpty(Errors(v, new OrderingOptions { FreeDeliveryThreshold = 10001 }, "FreeDeliveryThreshold"));
        Assert.Empty(Errors(v, new OrderingOptions { FreeDeliveryThreshold = null }, "FreeDeliveryThreshold"));
        Assert.NotEmpty(Errors(v, new OrderingOptions { PausedMessage = "" }, "PausedMessage"));
        Assert.NotEmpty(Errors(v, new OrderingOptions { PaymentInstructions = " " }, "PaymentInstructions"));
    }

    // ---- Restaurant orders --------------------------------------------------------------------------------------------

    [Fact]
    public void Wholesale_Rules()
    {
        var v = new WholesaleOptionsValidator();
        Valid(v, new WholesaleOptions());
        Assert.Contains("The last time must be the same as or later than the first time.", Errors(v, new WholesaleOptions { FirstSlot = "15:00", LastSlot = "10:00" }, "LastSlot"));
        Assert.NotEmpty(Errors(v, new WholesaleOptions { MinimumQuantityPerItem = 0 }, "MinimumQuantityPerItem"));
        Assert.Empty(Errors(v, new WholesaleOptions { QuantityStep = 0 }, "QuantityStep"));
        Assert.NotEmpty(Errors(v, new WholesaleOptions { DiscountPercent = 91 }, "DiscountPercent"));
        Assert.NotEmpty(Errors(v, new WholesaleOptions { GenerateDaysAhead = 0 }, "GenerateDaysAhead"));
        Assert.NotEmpty(Errors(v, new WholesaleOptions { ChangeCutoffHours = 169 }, "ChangeCutoffHours"));
        Assert.NotEmpty(Errors(v, new WholesaleOptions { PaymentTerms = "" }, "PaymentTerms"));
    }

    // ---- Email ------------------------------------------------------------------------------------------------------

    private static EmailOptions Email(Action<EmailOptions>? change = null)
    {
        var options = new EmailOptions { AdminRecipients = ["owner@shutki.test"] };
        change?.Invoke(options);
        return options;
    }

    [Fact]
    public void Email_Defaults_AreValid() => Valid(new EmailOptionsValidator(), Email());

    [Theory]
    [InlineData("https://smtp.gmail.com")]
    [InlineData("smtp://smtp.gmail.com")]
    [InlineData("smtp.gmail.com:587")]
    [InlineData("smtp gmail.com")]
    public void Email_SmtpHost_WithSchemePortOrSpaces_IsRejected(string host)
    {
        Assert.Contains(
            "Enter only the server name, e.g. smtp.gmail.com (no https:// and no port).",
            Errors(new EmailOptionsValidator(), Email(o => o.Smtp.Host = host), "Smtp.Host"));
    }

    [Theory]
    [InlineData("smtp.gmail.com")]
    [InlineData("127.0.0.1")]
    [InlineData("")]
    public void Email_SmtpHost_PlainNamesAreAccepted(string host)
    {
        Assert.Empty(Errors(new EmailOptionsValidator(), Email(o => o.Smtp.Host = host), "Smtp.Host"));
    }

    [Fact]
    public void Email_AdminRecipients_EachMustBeAnAddress_AtMost20()
    {
        var v = new EmailOptionsValidator();

        var bad = v.Validate(Email(o => o.AdminRecipients = ["owner@shutki.test", "not-an-email"])).Errors;
        var error = Assert.Single(bad);
        Assert.Equal("AdminRecipients[1]", error.PropertyName);
        Assert.Equal("'not-an-email' is not an email address.", error.ErrorMessage);

        var many = Enumerable.Range(1, 21).Select(i => $"cook{i}@shutki.test").ToList();
        Assert.Contains("Please list at most 20 addresses.", Errors(v, Email(o => o.AdminRecipients = many), "AdminRecipients"));
        Assert.Empty(Errors(v, Email(o => o.AdminRecipients = many.Take(20).ToList()), "AdminRecipients"));
        Assert.Empty(Errors(v, Email(o => o.AdminRecipients = []), "AdminRecipients"));
    }

    [Fact]
    public void Email_OtherRules()
    {
        var v = new EmailOptionsValidator();
        Assert.NotEmpty(Errors(v, Email(o => o.FromAddress = "kitchen"), "FromAddress"));
        Assert.NotEmpty(Errors(v, Email(o => o.FromName = ""), "FromName"));
        Assert.NotEmpty(Errors(v, Email(o => o.ReplyToAddress = "reply"), "ReplyToAddress"));
        Assert.Empty(Errors(v, Email(o => o.ReplyToAddress = ""), "ReplyToAddress"));
        Assert.Empty(Errors(v, Email(o => o.ReplyToAddress = null), "ReplyToAddress"));
        Assert.Contains("Choose a delivery method from the list.", Errors(v, Email(o => o.DeliveryMethod = "Pigeon"), "DeliveryMethod"));
        Assert.Empty(Errors(v, Email(o => o.DeliveryMethod = "pickupdirectory"), "DeliveryMethod"));
        Assert.Contains("Choose an encryption option from the list.", Errors(v, Email(o => o.Smtp.Security = "Tls"), "Smtp.Security"));
        Assert.Empty(Errors(v, Email(o => o.Smtp.Security = "none"), "Smtp.Security"));
        Assert.NotEmpty(Errors(v, Email(o => o.Smtp.Port = 0), "Smtp.Port"));
        Assert.NotEmpty(Errors(v, Email(o => o.Smtp.TimeoutSeconds = 301), "Smtp.TimeoutSeconds"));
    }

    // ---- Website ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("https://www.shutkivorta.com")]
    [InlineData("https://www.shutkivorta.com/")]
    [InlineData("http://localhost:5000")]
    public void Site_BaseUrl_JustTheAddress_IsAccepted(string url)
    {
        Assert.Empty(Errors(new SiteOptionsValidator(), new SiteOptions { BaseUrl = url }, "BaseUrl"));
    }

    [Theory]
    [InlineData("https://www.shutkivorta.com/shop")]
    [InlineData("https://www.shutkivorta.com/shop/")]
    [InlineData("https://www.shutkivorta.com/?ref=1")]
    [InlineData("www.shutkivorta.com")]
    [InlineData("ftp://shutkivorta.com")]
    public void Site_BaseUrl_WithAPathQueryOrNoScheme_IsRejected(string url)
    {
        Assert.Contains(
            "Enter just your secure web address, e.g. https://www.yourdomain.com (https, no page path).",
            Errors(new SiteOptionsValidator(), new SiteOptions { BaseUrl = url }, "BaseUrl"));
    }

    [Fact]
    public void Site_OtherRules()
    {
        var v = new SiteOptionsValidator();
        Valid(v, new SiteOptions());
        Assert.Contains("Use a site path such as /images/photo.jpg or a full https:// address.", Errors(v, new SiteOptions { DefaultSocialImage = "images/og.jpg" }, "DefaultSocialImage"));
        Assert.Empty(Errors(v, new SiteOptions { DefaultSocialImage = "https://cdn.shutki.test/og.jpg" }, "DefaultSocialImage"));
        Assert.NotEmpty(Errors(v, new SiteOptions { DefaultMetaDescription = "" }, "DefaultMetaDescription"));
        Assert.NotEmpty(Errors(v, new SiteOptions { DefaultMetaDescription = new string('x', 301) }, "DefaultMetaDescription"));

        var paths = v.Validate(new SiteOptions { AdditionalDisallowedPaths = ["/drafts", "drafts", "/my drafts"] }).Errors;
        Assert.Equal(["AdditionalDisallowedPaths[1]", "AdditionalDisallowedPaths[2]"], paths.Select(e => e.PropertyName));
        Assert.Equal("'drafts' must be a path starting with /, e.g. /drafts.", paths[0].ErrorMessage);
    }

    // ---- Spam protection and accounts ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(3, 1, true)]
    [InlineData(1000, 1440, true)]
    [InlineData(2, 10, false)]
    [InlineData(0, 10, false)]
    [InlineData(1001, 10, false)]
    [InlineData(10, 0, false)]
    [InlineData(10, 1441, false)]
    public void RateLimiting_Ranges(int posts, int minutes, bool valid)
    {
        Assert.Equal(valid, new RateLimitingOptionsValidator().Validate(new RateLimitingOptions { FormPostsPerWindow = posts, WindowMinutes = minutes }).IsValid);
    }

    [Fact]
    public void Accounts_HaveNoExtraRules()
    {
        Assert.True(new AccountOptionsValidator().Validate(new AccountOptions { RequireConfirmedEmail = true }).IsValid);
    }
}
