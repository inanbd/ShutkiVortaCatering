using ShutkiVorta.Application.Common.Settings;
using ShutkiVorta.Application.Features.Settings;
using static ShutkiVorta.UnitTests.Application.Settings.SettingsTestHelpers;

namespace ShutkiVorta.UnitTests.Application.Settings;

/// <summary>What admins see and type ↔ what is stored, per kind of setting.</summary>
public sealed class SettingValueFormatTests
{
    private static SettingField Field(string key) => AllFields.Single(f => f.Key == key);

    private static (bool Ok, string? Canonical, string Error) Parse(string key, string? raw, bool nullable = false)
    {
        var ok = SettingValueFormat.TryParse(Field(key), raw, nullable, out var canonical, out var error);
        return (ok, canonical, error);
    }

    private static string? Canonical(string key, string? raw, bool nullable = false)
    {
        var (ok, canonical, error) = Parse(key, raw, nullable);
        Assert.True(ok, $"'{raw}' for {key} was rejected: {error}");
        Assert.Equal(string.Empty, error);
        return canonical;
    }

    private static string Error(string key, string? raw, bool nullable = false)
    {
        var (ok, _, error) = Parse(key, raw, nullable);
        Assert.False(ok, $"'{raw}' for {key} should be rejected.");
        Assert.False(string.IsNullOrWhiteSpace(error));
        return error;
    }

    // ---- Percent: stored as a fraction, shown and typed as a percentage ------------------------------------------

    [Theory]
    [InlineData("0.0825", "8.25")]
    [InlineData("0.08", "8")]
    [InlineData("0.25", "25")]
    [InlineData("0", "0")]
    [InlineData("0.08125", "8.125")]
    public void Percent_IsShownAsAPercentage(string stored, string shown)
    {
        Assert.Equal(shown, SettingValueFormat.ToDisplay(Field("Ordering:TaxRate"), stored));
    }

    [Theory]
    [InlineData("8.25", "0.0825")]
    [InlineData(" 8.25 ", "0.0825")]
    [InlineData("8.25%", "0.0825")]
    [InlineData("8", "0.08")]
    [InlineData("0", "0")]
    [InlineData("25", "0.25")]
    [InlineData("8.125", "0.08125")]
    public void Percent_IsTypedAsAPercentage_AndStoredAsAFraction(string typed, string stored)
    {
        Assert.Equal(stored, Canonical("Ordering:TaxRate", typed));
    }

    [Fact]
    public void Percent_RoundTripsThroughTheEditor()
    {
        var field = Field("Ordering:TaxRate");
        foreach (var stored in new[] { "0.0825", "0.0625", "0.1", "0" })
        {
            var shown = SettingValueFormat.ToDisplay(field, stored);
            Assert.Equal(decimal.Parse(stored, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(Canonical("Ordering:TaxRate", shown)!, System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Theory]
    [InlineData("26")]
    [InlineData("-1")]
    [InlineData("100")]
    public void Percent_OutsideTheRange_IsRejected(string typed)
    {
        Assert.Equal("Please enter a value between 0 and 25.", Error("Ordering:TaxRate", typed));
    }

    [Fact]
    public void Percent_Garbage_IsRejected() =>
        Assert.Equal("Please enter a number, e.g. 12.50.", Error("Ordering:TaxRate", "eight"));

    [Fact]
    public void ToDisplay_LeavesOtherKindsAndUnparsableValuesAlone()
    {
        Assert.Equal("10", SettingValueFormat.ToDisplay(Field("Ordering:DeliveryFee"), "10"));
        Assert.Equal("11:00", SettingValueFormat.ToDisplay(Field("Ordering:FirstSlot"), "11:00"));
        Assert.Equal(string.Empty, SettingValueFormat.ToDisplay(Field("Ordering:TaxRate"), string.Empty));
        Assert.Null(SettingValueFormat.ToDisplay(Field("Ordering:TaxRate"), null));
    }

    // ---- Money and decimals ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("$12.50", "12.50")]
    [InlineData("12.50", "12.50")]
    [InlineData("$ 12.5", "12.5")]
    [InlineData("10", "10")]
    [InlineData(" $0 ", "0")]
    public void Money_AcceptsADollarSign(string typed, string stored)
    {
        Assert.Equal(stored, Canonical("Ordering:DeliveryFee", typed));
    }

    [Fact]
    public void Money_UsesTheInvariantCulture_RegardlessOfTheServerCulture()
    {
        WithCulture("de-DE", () =>
        {
            Assert.Equal("1234.5", Canonical("Ordering:FreeDeliveryThreshold", "1,234.5", nullable: true));
            Assert.Equal("12.5", Canonical("Ordering:DeliveryFee", "12.5"));
        });
    }

    [Theory]
    [InlineData("501")]
    [InlineData("-0.01")]
    public void Money_OutsideTheRange_IsRejected(string typed)
    {
        Assert.Equal("Please enter a value between 0 and 500.", Error("Ordering:DeliveryFee", typed));
    }

    [Fact]
    public void EmptyValue_IsAllowedForNullableSettings_AndRequiredOtherwise()
    {
        Assert.Equal(string.Empty, Canonical("Ordering:FreeDeliveryThreshold", "", nullable: true));
        Assert.Equal(string.Empty, Canonical("Ordering:FreeDeliveryThreshold", "   ", nullable: true));
        Assert.Equal(string.Empty, Canonical("Business:Latitude", null, nullable: true));

        Assert.Equal("This field is required.", Error("Ordering:DeliveryFee", ""));
        Assert.Equal("This field is required.", Error("Ordering:MaxDaysInAdvance", null));
        Assert.Equal("This field is required.", Error("Ordering:TaxRate", " "));
    }

    [Theory]
    [InlineData("32.7767", "32.7767")]
    [InlineData("-90", "-90")]
    [InlineData("90", "90")]
    public void Decimal_WithinTheRange_IsAccepted(string typed, string stored)
    {
        Assert.Equal(stored, Canonical("Business:Latitude", typed, nullable: true));
    }

    [Theory]
    [InlineData("Business:Latitude", "90.5", "Please enter a value between -90 and 90.")]
    [InlineData("Business:Longitude", "-180.1", "Please enter a value between -180 and 180.")]
    [InlineData("Wholesale:MinimumQuantityPerItem", "0.25", "Please enter a value between 0.5 and 1000.")]
    public void Decimal_OutsideTheRange_IsRejected(string key, string typed, string message)
    {
        Assert.Equal(message, Error(key, typed, nullable: key.StartsWith("Business:", StringComparison.Ordinal)));
    }

    // ---- Whole numbers ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("24", "24")]
    [InlineData(" 48 ", "48")]
    [InlineData("0", "0")]
    [InlineData("720", "720")]
    public void Integer_IsAccepted(string typed, string stored)
    {
        Assert.Equal(stored, Canonical("Ordering:MinimumLeadTimeHours", typed));
    }

    [Theory]
    [InlineData("24.5")]
    [InlineData("twenty")]
    [InlineData("1e3")]
    public void Integer_RejectsNonWholeNumbers(string typed)
    {
        Assert.Equal("Please enter a whole number.", Error("Ordering:MinimumLeadTimeHours", typed));
    }

    [Theory]
    [InlineData("Ordering:MinimumLeadTimeHours", "721", "Please enter a value between 0 and 720.")]
    [InlineData("Ordering:MinimumLeadTimeHours", "-1", "Please enter a value between 0 and 720.")]
    [InlineData("Email:Smtp:Port", "0", "Please enter a value between 1 and 65535.")]
    [InlineData("Email:Smtp:Port", "65536", "Please enter a value between 1 and 65535.")]
    [InlineData("RateLimiting:FormPostsPerWindow", "0", "Please enter a value between 3 and 1000.")]
    public void Integer_OutsideTheRange_IsRejected(string key, string typed, string message)
    {
        Assert.Equal(message, Error(key, typed));
    }

    // ---- Times --------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("9:5", "09:05")]
    [InlineData("09:05", "09:05")]
    [InlineData("9:00", "09:00")]
    [InlineData(" 17:30 ", "17:30")]
    [InlineData("23:59", "23:59")]
    [InlineData("00:00", "00:00")]
    public void Time_IsNormalisedToHHmm(string typed, string stored)
    {
        Assert.Equal(stored, Canonical("Ordering:FirstSlot", typed));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("25:00")]
    [InlineData("noon")]
    [InlineData("12:60")]
    public void Time_Invalid_IsRejected(string? typed)
    {
        Assert.Equal("Please enter a time such as 09:30 or 17:00.", Error("Wholesale:LastSlot", typed));
    }

    // ---- Booleans -----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("true", "true")]
    [InlineData("True", "true")]
    [InlineData("on", "true")]
    [InlineData("false", "false")]
    [InlineData("", "false")]
    [InlineData(null, "false")]
    [InlineData("yes please", "false")]
    public void Bool_CheckedIsTrue_AbsentIsFalse(string? posted, string stored)
    {
        Assert.Equal(stored, Canonical("Ordering:AcceptingOrders", posted));
    }

    // ---- Choices --------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("StartTls", "StartTls")]
    [InlineData("starttls", "StartTls")]
    [InlineData(" None ", "None")]
    public void Select_AcceptsListedChoices_UsingTheirCanonicalSpelling(string posted, string stored)
    {
        Assert.Equal(stored, Canonical("Email:Smtp:Security", posted));
    }

    [Theory]
    [InlineData("Tls")]
    [InlineData("")]
    public void Select_RejectsUnlistedValues(string posted)
    {
        Assert.Equal("Please choose an option from the list.", Error("Email:Smtp:Security", posted));
    }

    [Fact]
    public void TimeZone_IsNormalisedToTheListedId_AndUnlistedIdsAreLeftToTheValidator()
    {
        Assert.Equal("America/Chicago", Canonical("Business:TimeZoneId", "america/chicago"));
        Assert.Equal("Mars/Olympus_Mons", Canonical("Business:TimeZoneId", "Mars/Olympus_Mons"));
    }

    // ---- Text -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Text_IsTrimmed_AndNullBecomesEmpty()
    {
        Assert.Equal("(214) 555-0199", Canonical("Business:Phone", "  (214) 555-0199  "));
        Assert.Equal(string.Empty, Canonical("Business:StreetAddress", null));
        Assert.Equal("Line one\r\nLine two", Canonical("Ordering:PaymentInstructions", "\r\nLine one\r\nLine two\r\n"));
    }
}
