using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Common.Settings;

namespace ShutkiVorta.UnitTests.Application.Settings;

/// <summary>Stored settings that cannot be used are dropped (and reported) instead of breaking the site.</summary>
public sealed class SettingsSanitizerTests
{
    private static Dictionary<string, string?> Stored(params (string Key, string? Value)[] rows) =>
        rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void GoodValues_AreKept_AndUnknownKeysAreIgnoredSilently()
    {
        var (clean, rejected) = SettingsSanitizer.Sanitize(Stored(
            ("Ordering:TaxRate", "0.0825"),
            ("Ordering:ClosedDays:0", "Monday"),
            ("Ordering:NoLongerASetting", "x"),
            ("Database:Provider", "Sqlite"),
            ("Email:Smtp:Password", "enc:v1:abc")));

        Assert.Empty(rejected);
        Assert.Equal(["Email:Smtp:Password", "Ordering:ClosedDays:0", "Ordering:TaxRate"], clean.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ValuesThatCannotBeConverted_AreDropped_SoTheDefaultApplies()
    {
        var (clean, rejected) = SettingsSanitizer.Sanitize(Stored(
            ("Ordering:TaxRate", "eight percent"),
            ("Ordering:MaxDaysInAdvance", "45"),
            ("Ordering:ClosedDays:0", "Funday"),
            ("Ordering:ClosedDays:1", "Tuesday"),
            ("Business:Name", "Shutki Vorta")));

        Assert.Equal(["Ordering:TaxRate"], rejected);
        Assert.Equal("45", clean["Ordering:MaxDaysInAdvance"]);
        Assert.Equal("Tuesday", clean["Ordering:ClosedDays:1"]);
        Assert.Equal("Shutki Vorta", clean["Business:Name"]);

        // What is left binds without errors (the binder itself skips list items it cannot convert).
        var ordering = (OrderingOptions)SettingsFlattener.Bind(typeof(OrderingOptions), "Ordering", clean);
        Assert.Equal(45, ordering.MaxDaysInAdvance);
        Assert.Equal(new OrderingOptions().TaxRate, ordering.TaxRate);
        Assert.Equal([DayOfWeek.Tuesday], ordering.ClosedDays);
    }

    [Fact]
    public void Nulls_AreRejectedForNumbersSwitchesAndListItems_ButMeanEmptyForText()
    {
        var (clean, rejected) = SettingsSanitizer.Sanitize(Stored(
            ("Ordering:MaxDaysInAdvance", null),
            ("Ordering:AcceptingOrders", null),
            ("Ordering:DeliveryZipPrefixes:0", null),
            ("Email:ReplyToAddress", null)));

        Assert.Equal(["Ordering:AcceptingOrders", "Ordering:DeliveryZipPrefixes:0", "Ordering:MaxDaysInAdvance"], rejected.Order(StringComparer.Ordinal));
        Assert.Equal(string.Empty, clean["Email:ReplyToAddress"]);
        Assert.True(string.IsNullOrEmpty(((EmailOptions)SettingsFlattener.Bind(typeof(EmailOptions), "Email", clean)).ReplyToAddress));
    }

    [Fact]
    public void KeysAreMatchedWhateverTheirCase()
    {
        var (clean, rejected) = SettingsSanitizer.Sanitize(Stored(("ORDERING:TAXRATE", "0.07"), ("ordering:closeddays:0", "Sunday")));

        Assert.Empty(rejected);
        var ordering = (OrderingOptions)SettingsFlattener.Bind(typeof(OrderingOptions), "Ordering", clean);
        Assert.Equal(0.07m, ordering.TaxRate);
        Assert.Equal([DayOfWeek.Sunday], ordering.ClosedDays);
    }
}
