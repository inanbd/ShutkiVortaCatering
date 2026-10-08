namespace ShutkiVorta.Application.Common.Options;

/// <summary>Rules for scheduling, fees and taxes applied at checkout.</summary>
public sealed class OrderingOptions
{
    public const string SectionName = "Ordering";

    public bool AcceptingOrders { get; set; } = true;
    public string PausedMessage { get; set; } = "We are not taking new orders right now. Please check back soon!";

    /// <summary>How many hours ahead an order must be placed (vortas are made fresh to order).</summary>
    public int MinimumLeadTimeHours { get; set; } = 24;

    public int MaxDaysInAdvance { get; set; } = 30;

    /// <summary>First pickup/delivery slot of the day, 24h "HH:mm".</summary>
    public string FirstSlot { get; set; } = "11:00";

    /// <summary>Last pickup/delivery slot of the day, 24h "HH:mm".</summary>
    public string LastSlot { get; set; } = "19:00";

    public int SlotIntervalMinutes { get; set; } = 60;
    public List<DayOfWeek> ClosedDays { get; set; } = [];

    public decimal DeliveryFee { get; set; } = 10m;
    public decimal? FreeDeliveryThreshold { get; set; } = 150m;
    public decimal MinimumDeliverySubtotal { get; set; } = 40m;
    public decimal TaxRate { get; set; } = 0.0825m;
    public bool TaxDeliveryFee { get; set; } = true;

    /// <summary>ZIP code prefixes we deliver to (e.g. "752" for Dallas). Empty means no restriction.</summary>
    public List<string> DeliveryZipPrefixes { get; set; } = [];

    public string DeliveryAreaDescription { get; set; } = "Dallas and nearby cities";
    public string PaymentInstructions { get; set; } = "Pay at pickup or on delivery with cash, Zelle or card.";
}
