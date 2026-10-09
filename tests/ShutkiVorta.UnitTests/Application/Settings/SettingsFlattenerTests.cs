using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Common.Settings;
using static ShutkiVorta.UnitTests.Application.Settings.SettingsTestHelpers;

namespace ShutkiVorta.UnitTests.Application.Settings;

/// <summary>Options objects ↔ flat configuration keys, with the same rules as the .NET configuration binder.</summary>
public sealed class SettingsFlattenerTests
{
    public static TheoryData<string> SectionNames => [.. ManagedSettings.Sections.Select(s => s.Name)];

    private static EmailOptions CustomEmail() => new()
    {
        Enabled = false,
        DeliveryMethod = EmailDeliveryMethods.Smtp,
        FromName = "Kitchen",
        FromAddress = "kitchen@shutki.test",
        ReplyToAddress = null,
        AdminRecipients = ["a@shutki.test", "b@shutki.test", "c@shutki.test"],
        SendCustomerStatusUpdates = false,
        Smtp = new SmtpSettings
        {
            Host = "smtp.shutki.test",
            Port = 465,
            Security = "SslOnConnect",
            UserName = "kitchen",
            Password = "p@ss:word",
            TimeoutSeconds = 45,
            AcceptInvalidCertificates = true,
            CheckCertificateRevocation = false,
            LocalDomain = null,
            PasswordUnreadable = true,
        },
    };

    [Fact]
    public void Flatten_WritesNestedSmtpKeys_ListsByIndex_AndSkipsComputedAndIgnoredProperties()
    {
        var flat = SettingsFlattener.Flatten(CustomEmail(), "Email");

        Assert.Equal("false", flat["Email:Enabled"]);
        Assert.Equal("Smtp", flat["Email:DeliveryMethod"]);
        Assert.Equal("smtp.shutki.test", flat["Email:Smtp:Host"]);
        Assert.Equal("465", flat["Email:Smtp:Port"]);
        Assert.Equal("true", flat["Email:Smtp:AcceptInvalidCertificates"]);
        Assert.Equal("false", flat["Email:Smtp:CheckCertificateRevocation"]);
        Assert.Equal("p@ss:word", flat["Email:Smtp:Password"]);
        Assert.Equal("a@shutki.test", flat["Email:AdminRecipients:0"]);
        Assert.Equal("c@shutki.test", flat["Email:AdminRecipients:2"]);
        Assert.False(flat.ContainsKey("Email:AdminRecipients:3"));
        Assert.False(flat.ContainsKey("Email:AdminRecipients"));
        Assert.False(flat.ContainsKey("Email:Smtp"));

        // Read-only (computed) and [SettingIgnore] properties are never stored.
        Assert.False(flat.ContainsKey("Email:Smtp:IsConfigured"));
        Assert.False(flat.ContainsKey("Email:Smtp:PasswordUnreadable"));
        Assert.DoesNotContain(flat.Keys, k => k.Contains("FullAddress") || k.Contains("NormalizedBaseUrl") || k.Contains("CityStateZip"));

        // Keys are looked up case-insensitively, like configuration keys.
        Assert.Equal("465", flat["email:smtp:port"]);
    }

    [Fact]
    public void Flatten_StoresNullsAsEmptyStrings_SoTheyOverrideClassDefaults()
    {
        var business = new BusinessOptions { Latitude = null, Longitude = null, StreetAddress = null, FacebookUrl = null };
        var ordering = new OrderingOptions { FreeDeliveryThreshold = null };

        var flatBusiness = SettingsFlattener.Flatten(business, "Business");
        var flatOrdering = SettingsFlattener.Flatten(ordering, "Ordering");

        Assert.Equal(string.Empty, flatBusiness["Business:Latitude"]);
        Assert.Equal(string.Empty, flatBusiness["Business:StreetAddress"]);
        Assert.Equal(string.Empty, flatOrdering["Ordering:FreeDeliveryThreshold"]);

        var bound = SettingsFlattener.Bind<BusinessOptions>("Business", flatBusiness);
        Assert.Null(bound.Latitude); // the class default is 32.7767
        Assert.Null(bound.Longitude);
        Assert.Null(SettingsFlattener.Bind<OrderingOptions>("Ordering", flatOrdering).FreeDeliveryThreshold); // class default 150
    }

    [Fact]
    public void Flatten_FormatsNumbersWithTheInvariantCulture_AndBindReadsThemBack()
    {
        WithCulture("de-DE", () =>
        {
            var ordering = new OrderingOptions { TaxRate = 0.0825m, DeliveryFee = 12.5m, FreeDeliveryThreshold = 1234.56m };
            var business = new BusinessOptions { Latitude = 32.7767, Longitude = -96.797 };

            var flatOrdering = SettingsFlattener.Flatten(ordering, "Ordering");
            var flatBusiness = SettingsFlattener.Flatten(business, "Business");

            Assert.Equal("0.0825", flatOrdering["Ordering:TaxRate"]);
            Assert.Equal("12.5", flatOrdering["Ordering:DeliveryFee"]);
            Assert.Equal("1234.56", flatOrdering["Ordering:FreeDeliveryThreshold"]);
            Assert.Equal("32.7767", flatBusiness["Business:Latitude"]);
            Assert.Equal("-96.797", flatBusiness["Business:Longitude"]);

            var bound = SettingsFlattener.Bind<OrderingOptions>("Ordering", flatOrdering);
            Assert.Equal(0.0825m, bound.TaxRate);
            Assert.Equal(1234.56m, bound.FreeDeliveryThreshold);
            Assert.Equal(32.7767, SettingsFlattener.Bind<BusinessOptions>("Business", flatBusiness).Latitude);
        });
    }

    [Fact]
    public void Flatten_WritesBooleansAsLowercaseAndEnumsByName()
    {
        var ordering = new OrderingOptions { AcceptingOrders = false, TaxDeliveryFee = true, ClosedDays = [DayOfWeek.Monday, DayOfWeek.Sunday] };
        var flat = SettingsFlattener.Flatten(ordering, "Ordering");

        Assert.Equal("false", flat["Ordering:AcceptingOrders"]);
        Assert.Equal("true", flat["Ordering:TaxDeliveryFee"]);
        Assert.Equal("Monday", flat["Ordering:ClosedDays:0"]);
        Assert.Equal("Sunday", flat["Ordering:ClosedDays:1"]);
    }

    [Fact]
    public void RoundTrip_PreservesEveryValue_IncludingNestedSmtpListsAndDays()
    {
        var email = CustomEmail();
        var bound = SettingsFlattener.Bind<EmailOptions>("Email", SettingsFlattener.Flatten(email, "Email"));

        Assert.False(bound.Enabled);
        Assert.Equal("Smtp", bound.DeliveryMethod);
        Assert.Equal(["a@shutki.test", "b@shutki.test", "c@shutki.test"], bound.AdminRecipients);
        Assert.Equal("smtp.shutki.test", bound.Smtp.Host);
        Assert.Equal(465, bound.Smtp.Port);
        Assert.Equal("SslOnConnect", bound.Smtp.Security);
        Assert.Equal("p@ss:word", bound.Smtp.Password);
        Assert.Equal(45, bound.Smtp.TimeoutSeconds);
        Assert.True(bound.Smtp.AcceptInvalidCertificates);
        Assert.False(bound.Smtp.CheckCertificateRevocation);
        Assert.False(bound.Smtp.PasswordUnreadable); // runtime-only, never stored

        var ordering = new OrderingOptions
        {
            ClosedDays = [DayOfWeek.Sunday, DayOfWeek.Wednesday],
            BlackoutDates = ["2027-03-20", "2027-03-21"],
            DeliveryZipPrefixes = ["752"],
            FreeDeliveryThreshold = null,
            AcceptingOrders = false,
        };
        var boundOrdering = SettingsFlattener.Bind<OrderingOptions>("Ordering", SettingsFlattener.Flatten(ordering, "Ordering"));
        Assert.Equal([DayOfWeek.Sunday, DayOfWeek.Wednesday], boundOrdering.ClosedDays);
        Assert.Equal(["2027-03-20", "2027-03-21"], boundOrdering.BlackoutDates);
        Assert.Equal(["752"], boundOrdering.DeliveryZipPrefixes);
        Assert.Null(boundOrdering.FreeDeliveryThreshold);
        Assert.False(boundOrdering.AcceptingOrders);
    }

    [Theory]
    [MemberData(nameof(SectionNames))]
    public void RoundTrip_OfTheDefaults_IsStable(string sectionName)
    {
        var section = ManagedSettings.Find(sectionName)!;
        var flat = SettingsFlattener.Flatten(section.CreateDefaults(), section.Name);
        var again = SettingsFlattener.Flatten(SettingsFlattener.Bind(section.OptionsType, section.Name, flat), section.Name);

        Assert.Equal(flat.OrderBy(kv => kv.Key), again.OrderBy(kv => kv.Key));
        Assert.All(flat.Keys, k => Assert.StartsWith(section.Name + ":", k, StringComparison.Ordinal));
    }

    [Fact]
    public void Bind_IgnoresKeysOfOtherSections_AndStartsFromClassDefaults()
    {
        var bound = SettingsFlattener.Bind<OrderingOptions>("Ordering", new Dictionary<string, string?>
        {
            ["Ordering:MaxDaysInAdvance"] = "45",
            ["Wholesale:LeadTimeDays"] = "9",
            ["Business:Name"] = "Elsewhere",
        });

        Assert.Equal(45, bound.MaxDaysInAdvance);
        Assert.Equal(24, bound.MinimumLeadTimeHours); // class default
        Assert.Empty(bound.ClosedDays); // class default (new-installation defaults come from ManagedSettings)
    }

    [Theory]
    [InlineData("Ordering", "Ordering:MaxDaysInAdvance", "thirty")]
    [InlineData("Ordering", "Ordering:TaxRate", "8.25%")]
    [InlineData("Ordering", "Ordering:AcceptingOrders", "maybe")]
    [InlineData("Email", "Email:Smtp:Port", "smtp")]
    [InlineData("Business", "Business:Latitude", "north")]
    public void Bind_InvalidValue_ThrowsSettingsBindingException_NamingTheKey(string section, string key, string value)
    {
        var type = ManagedSettings.Find(section)!.OptionsType;

        var ex = Assert.Throws<SettingsBindingException>(() => SettingsFlattener.Bind(type, section, new Dictionary<string, string?> { [key] = value }));

        Assert.Equal(key, ex.Key, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(key, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact]
    public void Bind_SkipsListItemsThatCannotBeConverted()
    {
        // Same as the .NET configuration binder: an unknown day name is dropped rather than failing the whole section.
        var bound = SettingsFlattener.Bind<OrderingOptions>("Ordering", new Dictionary<string, string?>
        {
            ["Ordering:ClosedDays:0"] = "Funday",
            ["Ordering:ClosedDays:1"] = "tuesday",
        });

        Assert.Equal([DayOfWeek.Tuesday], bound.ClosedDays);
    }

    [Fact]
    public void SettingsBindingException_WithoutAKey_StillHasAMessage()
    {
        var ex = new SettingsBindingException(null, new InvalidOperationException("boom"));
        Assert.Null(ex.Key);
        Assert.Equal("The value for a setting is not valid.", ex.Message);
    }

    [Fact]
    public void DescribeProperties_ListsLeafPaths_WithListsOnceAndNestedObjectsExpanded()
    {
        var email = SettingsFlattener.DescribeProperties(typeof(EmailOptions), "Email").ToDictionary(p => p.Path, p => p.Type);

        Assert.Equal(typeof(int), email["Email:Smtp:Port"]);
        Assert.Equal(typeof(string), email["Email:Smtp:Password"]);
        Assert.Equal(typeof(List<string>), email["Email:AdminRecipients"]);
        Assert.False(email.ContainsKey("Email:Smtp"));
        Assert.False(email.ContainsKey("Email:Smtp:PasswordUnreadable"));
        Assert.False(email.ContainsKey("Email:Smtp:IsConfigured"));
        Assert.DoesNotContain(email.Keys, k => k.StartsWith("Email:AdminRecipients:", StringComparison.Ordinal));

        var ordering = SettingsFlattener.DescribeProperties(typeof(OrderingOptions), "Ordering").ToDictionary(p => p.Path, p => p.Type);
        Assert.Equal(typeof(List<DayOfWeek>), ordering["Ordering:ClosedDays"]);
        Assert.Equal(typeof(decimal?), ordering["Ordering:FreeDeliveryThreshold"]);
        Assert.Equal(typeof(double?), SettingsFlattener.DescribeProperties(typeof(BusinessOptions), "Business").Single(p => p.Path == "Business:Latitude").Type);
    }

    [Fact]
    public void TypeHelpers_RecogniseListsAndTheirElements()
    {
        Assert.False(SettingsFlattener.IsListType(typeof(string)));
        Assert.True(SettingsFlattener.IsListType(typeof(List<string>)));
        Assert.True(SettingsFlattener.IsListType(typeof(string[])));
        Assert.False(SettingsFlattener.IsListType(typeof(int)));
        Assert.Equal(typeof(DayOfWeek), SettingsFlattener.ElementType(typeof(List<DayOfWeek>)));
        Assert.Equal(typeof(string), SettingsFlattener.ElementType(typeof(string[])));
    }

    [Fact]
    public void Format_UsesStorageConventions()
    {
        WithCulture("fr-FR", () =>
        {
            Assert.Equal(string.Empty, SettingsFlattener.Format(null));
            Assert.Equal("true", SettingsFlattener.Format(true));
            Assert.Equal("false", SettingsFlattener.Format(false));
            Assert.Equal("Monday", SettingsFlattener.Format(DayOfWeek.Monday));
            Assert.Equal("1234.5", SettingsFlattener.Format(1234.5m));
            Assert.Equal("-96.797", SettingsFlattener.Format(-96.797));
            Assert.Equal("42", SettingsFlattener.Format(42));
            Assert.Equal("text", SettingsFlattener.Format("text"));
        });
    }
}
