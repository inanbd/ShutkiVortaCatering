using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Domain.Common;
using ShutkiVorta.Domain.Menu;
using ShutkiVorta.UnitTests.TestDoubles;

namespace ShutkiVorta.UnitTests.Domain;

public sealed class MenuItemTests
{
    [Fact]
    public void Create_GeneratesSlugFromName_AndRoundsPrice()
    {
        var item = MenuItem.Create(new MenuItemDetails
        {
            Name = "Begun Vorta",
            ShortDescription = "Smoky eggplant",
            Description = "Charred eggplant mash",
            PricePerUnit = 13.999m,
        }, TestData.Now);

        Assert.Equal("begun-vorta", item.Slug);
        Assert.Equal(14.00m, item.PricePerUnit);
        Assert.Equal("lb", item.Unit);
        Assert.True(item.IsAvailable);
    }

    [Fact]
    public void Create_RejectsNonPositivePrice() =>
        Assert.Throws<DomainException>(() => MenuItem.Create(new MenuItemDetails
        {
            Name = "Free Vorta",
            ShortDescription = "x",
            Description = "x",
            PricePerUnit = 0m,
        }, TestData.Now));

    [Fact]
    public void Create_RejectsSpiceLevelOutOfRange() =>
        Assert.Throws<DomainException>(() => MenuItem.Create(new MenuItemDetails
        {
            Name = "Volcano Vorta",
            ShortDescription = "x",
            Description = "x",
            PricePerUnit = 10m,
            SpiceLevel = 9,
        }, TestData.Now));

    [Theory]
    [InlineData(0.5, true)]
    [InlineData(1.0, true)]
    [InlineData(2.5, true)]
    [InlineData(0.25, false)]
    [InlineData(1.2, false)]
    public void ValidateQuantity_EnforcesMinimumAndStep(decimal quantity, bool valid)
    {
        var item = TestData.Item(1);
        Assert.Equal(valid, item.ValidateQuantity(quantity) is null);
    }

    [Theory]
    [InlineData(null, 21.24)] // retail 24.99 less the default 15%
    [InlineData(19.5, 19.50)]
    public void EffectiveWholesalePrice_UsesExplicitPriceOrDefaultDiscount(double? wholesale, double expected)
    {
        var item = MenuItem.Create(new MenuItemDetails
        {
            Name = "Loitta Shutki Vorta",
            ShortDescription = "x",
            Description = "x",
            PricePerUnit = 24.99m,
            WholesalePricePerUnit = (decimal?)wholesale,
        }, TestData.Now);

        Assert.Equal((decimal)expected, item.EffectiveWholesalePrice(15m));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(18.5, true)]
    [InlineData(0.0, false)]
    [InlineData(-3.0, false)]
    public void SaveMenuItemValidator_AllowsBlankOrPositiveWholesalePrice(double? wholesale, bool valid)
    {
        var command = new SaveMenuItemCommand
        {
            Name = "Shim Vorta",
            ShortDescription = "Green beans",
            Description = "Mashed flat beans",
            PricePerUnit = 13.99m,
            WholesalePricePerUnit = (decimal?)wholesale,
        };

        var result = new SaveMenuItemCommandValidator().Validate(command);

        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void SetAvailability_UpdatesTimestamp()
    {
        var item = TestData.Item(1);
        var later = TestData.Now.AddHours(1);

        item.SetAvailability(false, later);

        Assert.False(item.IsAvailable);
        Assert.Equal(later, item.UpdatedAtUtc);
    }
}
