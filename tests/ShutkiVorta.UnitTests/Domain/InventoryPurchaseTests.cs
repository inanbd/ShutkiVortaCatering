using ShutkiVorta.Domain.Common;
using ShutkiVorta.Domain.Inventory;
using ShutkiVorta.UnitTests.TestDoubles;

namespace ShutkiVorta.UnitTests.Domain;

public sealed class InventoryPurchaseTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(TestData.Now);

    private static InventoryPurchase Purchase(params InventoryLineRequest[] lines) =>
        InventoryPurchase.Record(Today, "Desi grocery", null, lines, "admin@test.local", Today, TestData.Now);

    [Fact]
    public void Record_TotalsTheLines_AndTidiesNamesAndUnits()
    {
        var purchase = Purchase(
            new InventoryLineRequest("  Mustard   oil ", 4m, " Bottle ", 27.956m),
            new InventoryLineRequest("Garlic", 2.5m, "lb", 6.25m));

        Assert.Equal(34.21m, purchase.Total);
        var oil = purchase.Lines[0];
        Assert.Equal("Mustard oil", oil.ItemName);
        Assert.Equal("bottle", oil.Unit);
        Assert.Equal(27.96m, oil.Price);
        Assert.Equal(6.99m, oil.PricePerUnit);
        Assert.Equal(2.50m, purchase.Lines[1].PricePerUnit);
        Assert.Equal(Today.ToDateTime(TimeOnly.MinValue), purchase.PurchasedOn);
    }

    [Fact]
    public void Record_RejectsAPurchaseDatedInTheFuture() =>
        Assert.Throws<DomainException>(() => InventoryPurchase.Record(
            Today.AddDays(1), null, null, [new("Salt", 1m, "lb", 1m)], "admin@test.local", Today, TestData.Now));

    [Fact]
    public void Record_NeedsAtLeastOneItem() =>
        Assert.Throws<DomainException>(() => Purchase());

    [Theory]
    [InlineData(0, 5)]
    [InlineData(-1, 5)]
    [InlineData(1, -0.01)]
    public void Record_RejectsANonPositiveQuantityOrNegativePrice(decimal quantity, decimal price) =>
        Assert.Throws<DomainException>(() => Purchase(new InventoryLineRequest("Salt", quantity, "lb", price)));

    [Fact]
    public void Record_AllowsAFreeItem() =>
        Assert.Equal(0m, Purchase(new InventoryLineRequest("Banana leaves", 10m, "piece", 0m)).Total);

    [Fact]
    public void Update_ReplacesTheLines_AndKeepsTheReceipts()
    {
        var purchase = Purchase(new InventoryLineRequest("Salt", 1m, "lb", 1m));
        purchase.AddReceipt("a.jpg", "receipt.jpg", "image/jpeg", 100, TestData.Now);

        purchase.Update(Today.AddDays(-1), "Farmers market", "paid cash", [new("Limes", 12m, "piece", 3m)], Today, TestData.Now);

        Assert.Equal("Limes", Assert.Single(purchase.Lines).ItemName);
        Assert.Equal(3m, purchase.Total);
        Assert.Equal("Farmers market", purchase.Store);
        Assert.Single(purchase.Receipts);
    }

    [Fact]
    public void AddReceipt_AllowsAtMostTheMaximum()
    {
        var purchase = Purchase(new InventoryLineRequest("Salt", 1m, "lb", 1m));
        for (var i = 0; i < InventoryPurchase.MaxReceipts; i++)
        {
            purchase.AddReceipt($"{i}.jpg", null, "image/jpeg", 100, TestData.Now);
        }

        Assert.Throws<DomainException>(() => purchase.AddReceipt("extra.jpg", null, "image/jpeg", 100, TestData.Now));
    }

    [Fact]
    public void RemoveReceipt_OnlyRemovesSavedReceiptsOfThisPurchase()
    {
        var purchase = Purchase(new InventoryLineRequest("Salt", 1m, "lb", 1m));
        var receipt = purchase.AddReceipt("a.jpg", null, "image/jpeg", 100, TestData.Now);
        receipt.AssignId(7);

        Assert.Null(purchase.RemoveReceipt(8, TestData.Now));
        Assert.Same(receipt, purchase.RemoveReceipt(7, TestData.Now));
        Assert.Empty(purchase.Receipts);
    }

    [Theory]
    [InlineData(@"C:\fakepath\IMG_0412.jpg", "IMG_0412.jpg")]
    [InlineData("photos/receipt.png", "receipt.png")]
    [InlineData("  ", null)]
    public void Receipt_KeepsOnlyTheUploadedFileName(string uploaded, string? expected)
    {
        var purchase = Purchase(new InventoryLineRequest("Salt", 1m, "lb", 1m));
        Assert.Equal(expected, purchase.AddReceipt("a.jpg", uploaded, "image/jpeg", 100, TestData.Now).OriginalFileName);
    }

    [Theory]
    [InlineData("mustard oil", "MUSTARD OIL")]
    [InlineData("  Mustard \t Oil ", "MUSTARD OIL")]
    [InlineData("লইট্টা শুঁটকি", "লইট্টা শুঁটকি")]
    public void ItemNames_MatchRegardlessOfCaseAndSpacing(string typed, string normalized) =>
        Assert.Equal(normalized, InventoryItem.Normalize(typed));

    [Fact]
    public void Item_RequiresAName() =>
        Assert.Throws<DomainException>(() => InventoryItem.Create("   ", "lb", TestData.Now));
}
