using ShutkiVorta.Domain.Common;
using ShutkiVorta.Domain.Menu;

namespace ShutkiVorta.Domain.Orders;

/// <summary>A purchased menu item. Name and price are snapshotted so later menu edits never change past orders.</summary>
public sealed class OrderLine : Entity
{
    private OrderLine()
    {
    }

    public int OrderId { get; private set; }
    public int MenuItemId { get; private set; }
    public string ItemName { get; private set; } = string.Empty;
    public string? ItemBengaliName { get; private set; }
    public string Unit { get; private set; } = MenuItem.DefaultUnit;
    public decimal UnitPrice { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal LineTotal { get; private set; }

    internal static OrderLine From(MenuItem item, decimal quantity) => new()
    {
        MenuItemId = item.Id,
        ItemName = item.Name,
        ItemBengaliName = item.BengaliName,
        Unit = item.Unit,
        UnitPrice = item.PricePerUnit,
        Quantity = quantity,
        LineTotal = Money.Round(item.PricePerUnit * quantity),
    };

    /// <summary>Line for an order generated from a restaurant standing order, at the agreed wholesale price.</summary>
    internal static OrderLine FromAgreedPrice(int menuItemId, string itemName, string? itemBengaliName, string unit, decimal unitPrice, decimal quantity) => new()
    {
        MenuItemId = menuItemId,
        ItemName = itemName,
        ItemBengaliName = itemBengaliName,
        Unit = unit,
        UnitPrice = unitPrice,
        Quantity = quantity,
        LineTotal = Money.Round(unitPrice * quantity),
    };

    public void AttachTo(int orderId) => OrderId = orderId;
}
