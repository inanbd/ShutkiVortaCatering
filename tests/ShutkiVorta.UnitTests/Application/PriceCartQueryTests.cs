using Microsoft.Extensions.Options;
using NSubstitute;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.UnitTests.TestDoubles;

namespace ShutkiVorta.UnitTests.Application;

public sealed class PriceCartQueryTests
{
    [Fact]
    public async Task Handle_PricesLines_FlagsProblems_AndReportsMissingItems()
    {
        var menu = Substitute.For<IMenuItemRepository>();
        menu.GetByIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns([TestData.Item(1, price: 20m), TestData.Item(2, "Shim Vorta", 10m, available: false)]);
        var handler = new PriceCartQueryHandler(menu, Options.Create(new OrderingOptions { DeliveryFee = 10m, FreeDeliveryThreshold = 150m, TaxRate = 0.1m }));

        var quote = await handler.Handle(
            new PriceCartQuery([new CartLineInput(1, 1m), new CartLineInput(1, 1m), new CartLineInput(2, 1m), new CartLineInput(99, 1m)]),
            CancellationToken.None);

        Assert.Equal(2, quote.Lines.Count);
        Assert.Equal(2m, quote.Lines[0].Quantity);               // merged
        Assert.NotNull(quote.Lines[1].Error);                    // unavailable
        Assert.Equal([99], quote.MissingItemIds);
        Assert.Equal(40m, quote.Subtotal);                       // unavailable item excluded
        Assert.Equal(44m, quote.PickupTotals.Total);
        Assert.Equal(55m, quote.DeliveryTotals.Total);
        Assert.Equal(110m, quote.AmountToFreeDelivery);
        Assert.True(quote.HasErrors);
    }
}
