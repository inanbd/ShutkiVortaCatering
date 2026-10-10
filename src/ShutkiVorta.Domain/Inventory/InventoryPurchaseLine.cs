using ShutkiVorta.Domain.Common;

namespace ShutkiVorta.Domain.Inventory;

/// <summary>An item on a purchase: quantity, unit and the total price paid for it (as printed on the receipt).</summary>
public sealed class InventoryPurchaseLine : Entity
{
    public const decimal MaxQuantity = 100_000m;
    public const decimal MaxPrice = 100_000m;

    // Required by the data mapper.
    private InventoryPurchaseLine()
    {
    }

    public int PurchaseId { get; private set; }

    /// <summary>The saved item; assigned by the persistence layer, which creates the item the first time its name is used.</summary>
    public int InventoryItemId { get; private set; }

    /// <summary>The item's name. Read back in its current spelling, so renaming a saved item corrects every purchase.</summary>
    public string ItemName { get; private set; } = string.Empty;

    public decimal Quantity { get; private set; }
    public string Unit { get; private set; } = InventoryItem.DefaultUnit;

    /// <summary>Total paid for <see cref="Quantity"/>, not the price per unit.</summary>
    public decimal Price { get; private set; }

    public decimal PricePerUnit => Quantity > 0 ? Money.Round(Price / Quantity) : 0m;

    internal static InventoryPurchaseLine Create(InventoryLineRequest request)
    {
        var name = InventoryItem.CleanName(request.ItemName);
        if (request.Quantity is <= 0 or > MaxQuantity)
        {
            throw new DomainException($"{name}: please enter a quantity greater than 0 (and below 100,000).");
        }

        if (request.Price is < 0 or > MaxPrice)
        {
            throw new DomainException($"{name}: please enter the price paid, from $0 up to $100,000.");
        }

        return new InventoryPurchaseLine
        {
            ItemName = name,
            Quantity = Math.Round(request.Quantity, 3, MidpointRounding.AwayFromZero),
            Unit = InventoryItem.CleanUnit(request.Unit),
            Price = Money.Round(request.Price),
        };
    }

    public void AttachTo(int purchaseId) => PurchaseId = purchaseId;

    public void LinkTo(int inventoryItemId) => InventoryItemId = inventoryItemId;
}
