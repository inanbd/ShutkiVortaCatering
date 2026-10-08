namespace ShutkiVorta.Application.Common.Options;

/// <summary>Rules for restaurant (wholesale) standing orders, bound from the "Wholesale" section.</summary>
public sealed class WholesaleOptions
{
    public const string SectionName = "Wholesale";

    public bool AcceptingRequests { get; set; } = true;

    /// <summary>Discount off the retail price when an item has no explicit wholesale price (e.g. 15 = 15%).</summary>
    public decimal DiscountPercent { get; set; } = 15m;

    /// <summary>Smallest quantity of each vorta per delivery.</summary>
    public decimal MinimumQuantityPerItem { get; set; } = 5m;

    public decimal QuantityStep { get; set; } = 1m;

    /// <summary>Smallest food subtotal per delivery.</summary>
    public decimal MinimumSubtotalPerDelivery { get; set; } = 100m;

    public decimal DeliveryFee { get; set; }

    /// <summary>First delivery must be at least this many days after the request (time to review and plan).</summary>
    public int LeadTimeDays { get; set; } = 3;

    /// <summary>How many days ahead individual orders are generated from active standing orders.</summary>
    public int GenerateDaysAhead { get; set; } = 7;

    /// <summary>Run the background generator automatically (hourly). The admin can always generate on demand.</summary>
    public bool AutoGenerate { get; set; } = true;

    /// <summary>Deliveries closer than this many hours cannot be skipped, paused or changed online (the kitchen has started).</summary>
    public int ChangeCutoffHours { get; set; } = 24;

    /// <summary>First and last delivery time restaurants can choose, 24h "HH:mm".</summary>
    public string FirstSlot { get; set; } = "09:00";

    public string LastSlot { get; set; } = "17:00";
    public int SlotIntervalMinutes { get; set; } = 30;

    public string DeliveryAreaDescription { get; set; } = "Dallas–Fort Worth";
    public string PaymentTerms { get; set; } = "Weekly invoice, payable within 7 days by Zelle, check or card.";
}
