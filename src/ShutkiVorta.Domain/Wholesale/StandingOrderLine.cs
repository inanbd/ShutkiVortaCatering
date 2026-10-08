using ShutkiVorta.Domain.Common;

namespace ShutkiVorta.Domain.Wholesale;

/// <summary>A vorta delivered on every scheduled day, at the agreed wholesale price.</summary>
public sealed class StandingOrderLine : Entity
{
    private StandingOrderLine()
    {
    }

    public int StandingOrderId { get; private set; }
    public int MenuItemId { get; private set; }
    public string ItemName { get; private set; } = string.Empty;
    public string? ItemBengaliName { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public decimal Quantity { get; private set; }

    public decimal LineTotal => Money.Round(UnitPrice * Quantity);

    internal static StandingOrderLine Create(StandingOrderLineRequest request) => new()
    {
        MenuItemId = request.MenuItemId,
        ItemName = Guard.NotEmpty(request.ItemName, "Item name", 120),
        ItemBengaliName = request.ItemBengaliName,
        Unit = Guard.NotEmpty(request.Unit, "Unit", 20),
        UnitPrice = Money.Round(Guard.Positive(request.UnitPrice, "Price")),
        Quantity = Guard.Positive(request.Quantity, "Quantity"),
    };

    public void AttachTo(int standingOrderId) => StandingOrderId = standingOrderId;
}

public sealed record StandingOrderLineRequest(
    int MenuItemId,
    string ItemName,
    string? ItemBengaliName,
    string Unit,
    decimal UnitPrice,
    decimal Quantity);

/// <summary>Quantity and minimum-spend rules for restaurant orders.</summary>
public sealed record WholesaleRules(decimal MinimumQuantityPerItem, decimal QuantityStep, decimal MinimumSubtotalPerDelivery);

/// <summary>Audit trail entry (submitted, approved, paused, prices changed...).</summary>
public sealed class StandingOrderEvent : Entity
{
    private StandingOrderEvent()
    {
    }

    public int StandingOrderId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string ChangedBy { get; private set; } = string.Empty;
    public DateTime ChangedAtUtc { get; private set; }

    internal static StandingOrderEvent Record(string description, string changedBy, DateTime nowUtc) => new()
    {
        Description = description.Length > 500 ? description[..500] : description,
        ChangedBy = string.IsNullOrWhiteSpace(changedBy) ? "System" : changedBy,
        ChangedAtUtc = nowUtc,
    };

    public void AttachTo(int standingOrderId) => StandingOrderId = standingOrderId;
}
