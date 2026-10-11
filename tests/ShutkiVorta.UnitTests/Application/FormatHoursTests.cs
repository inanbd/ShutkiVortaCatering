using ShutkiVorta.Application.Common.Formatting;

namespace ShutkiVorta.UnitTests.Application;

public sealed class FormatHoursTests
{
    [Theory]
    [InlineData(7 * 60 + 45, "7 h 45 min", "7.75")]
    [InlineData(8 * 60, "8 h", "8.00")]
    [InlineData(45, "45 min", "0.75")]
    [InlineData(0, "0 min", "0.00")]
    [InlineData(1, "1 min", "0.02")]
    [InlineData(41 * 60 + 20, "41 h 20 min", "41.33")]
    public void HoursAndDecimalHours(int minutes, string hours, string decimalHours)
    {
        var duration = TimeSpan.FromMinutes(minutes).Add(TimeSpan.FromSeconds(59)); // seconds never round up

        Assert.Equal(hours, Format.Hours(duration));
        Assert.Equal(decimalHours, Format.DecimalHours(duration));
    }

    [Fact]
    public void NegativeDurations_CountAsZero()
    {
        Assert.Equal("0 min", Format.Hours(TimeSpan.FromMinutes(-5)));
        Assert.Equal("0.00", Format.DecimalHours(TimeSpan.FromMinutes(-5)));
    }
}
