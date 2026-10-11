using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Common.Settings;
using static ShutkiVorta.UnitTests.Application.Settings.SettingsTestHelpers;

namespace ShutkiVorta.UnitTests.Application.Settings;

/// <summary>Admin → Settings → Time clock.</summary>
public sealed class TimeClockSettingsTests
{
    [Fact]
    public void ANewSite_HasNoKitchenLocation_A300MetreRadius_MondayWeeks_AndLocationNotRequired()
    {
        var defaults = (TimeClockOptions)ManagedSettings.Find(TimeClockOptions.SectionName)!.CreateDefaults();

        Assert.False(defaults.HasKitchenLocation);
        Assert.Equal(300, defaults.NearKitchenRadiusMeters);
        Assert.Equal(DayOfWeek.Monday, defaults.WorkweekStartsOn);
        Assert.False(defaults.RequireLocation);
        Assert.True(Validate(defaults).IsValid);
    }

    [Fact]
    public void ThePage_IsSeparateFromThePublicBusinessCoordinates()
    {
        var page = SettingsCatalog.FindBySlug("time-clock")!;

        Assert.Equal(TimeClockOptions.SectionName, page.Section);
        Assert.Equal(
            ["TimeClock:KitchenLatitude", "TimeClock:KitchenLongitude", "TimeClock:NearKitchenRadiusMeters", "TimeClock:WorkweekStartsOn", "TimeClock:RequireLocation"],
            page.Fields.Select(f => f.Key));
        Assert.Contains("Monday", page.Fields.Single(f => f.Name == "WorkweekStartsOn").Help);
        Assert.NotNull(SettingsCatalog.FindBySlug("business")!.Fields.SingleOrDefault(f => f.Key == "Business:Latitude"));
    }

    [Fact]
    public void TheWorkweekStart_IsStoredAsTheDayName_AndBindsBack()
    {
        var flat = SettingsFlattener.Flatten(new TimeClockOptions { WorkweekStartsOn = DayOfWeek.Sunday, KitchenLatitude = 32.912345 }, TimeClockOptions.SectionName);
        Assert.Equal("Sunday", flat["TimeClock:WorkweekStartsOn"]);
        Assert.Equal(string.Empty, flat["TimeClock:KitchenLongitude"]);

        var bound = SettingsFlattener.Bind<TimeClockOptions>(TimeClockOptions.SectionName, flat);
        Assert.Equal(DayOfWeek.Sunday, bound.WorkweekStartsOn);
        Assert.Equal(32.912345, bound.KitchenLatitude);
        Assert.Null(bound.KitchenLongitude);
    }

    [Theory]
    [InlineData(32.9, null, "KitchenLongitude")]
    [InlineData(null, -96.8, "KitchenLatitude")]
    [InlineData(double.NaN, -96.8, "KitchenLatitude")]
    [InlineData(32.9, double.PositiveInfinity, "KitchenLongitude")]
    [InlineData(91.0, -96.8, "KitchenLatitude")]
    public void TheKitchenLocation_NeedsBothCoordinates_WithinRange(double? latitude, double? longitude, string invalid)
    {
        var errors = Validate(new TimeClockOptions { KitchenLatitude = latitude, KitchenLongitude = longitude }).Errors;

        Assert.Contains(errors, e => e.PropertyName == invalid);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(50_001)]
    public void TheRadius_IsBetween25MetresAnd50Kilometres(int radius)
    {
        Assert.Contains(Validate(new TimeClockOptions { NearKitchenRadiusMeters = radius }).Errors, e => e.PropertyName == "NearKitchenRadiusMeters");
        Assert.True(Validate(new TimeClockOptions { NearKitchenRadiusMeters = 25, KitchenLatitude = 32.9, KitchenLongitude = -96.8 }).IsValid);
    }
}
