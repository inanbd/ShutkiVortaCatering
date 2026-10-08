using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Domain.Menu;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.Domain.Wholesale;

namespace ShutkiVorta.Application.Features.Wholesale;

public sealed record WholesaleItemDto(
    int Id,
    string Name,
    string? BengaliName,
    string Slug,
    string? ImageUrl,
    string? ImageAlt,
    MenuCategory Category,
    string CategoryName,
    string ShortDescription,
    string Unit,
    decimal RetailPrice,
    decimal WholesalePrice,
    int SpiceLevel);

public sealed record WholesaleCatalogDto
{
    public bool AcceptingRequests { get; init; }
    public IReadOnlyList<WholesaleItemDto> Items { get; init; } = [];
    public decimal MinimumQuantityPerItem { get; init; }
    public decimal QuantityStep { get; init; }
    public decimal MinimumSubtotalPerDelivery { get; init; }
    public decimal DeliveryFee { get; init; }
    public decimal DiscountPercent { get; init; }
    public decimal TaxRate { get; init; }
    public DateOnly EarliestStartDate { get; init; }
    public IReadOnlyList<TimeOnly> TimeSlots { get; init; } = [];
    public IReadOnlyList<DayOfWeek> ClosedDays { get; init; } = [];
    public int LeadTimeDays { get; init; }
    public int ChangeCutoffHours { get; init; }
    public required string PaymentTerms { get; init; }
    public required string DeliveryAreaDescription { get; init; }
}

public sealed record StandingOrderLineDto(
    int MenuItemId,
    string ItemName,
    string? ItemBengaliName,
    string Unit,
    decimal UnitPrice,
    decimal Quantity,
    decimal LineTotal)
{
    public string QuantityText => Format.Quantity(Quantity, Unit);
}

public sealed record StandingOrderEventDto(string Description, string ChangedBy, DateTime ChangedAtLocal);

public enum DeliveryState
{
    Scheduled,
    OrderCreated,
    Skipped,
    Cancelled,
    KitchenClosed,
    Paused,
}

public sealed record UpcomingDeliveryDto(
    DateOnly Date,
    DateTime ScheduledFor,
    DeliveryState State,
    string? OrderNumber,
    OrderStatus? OrderStatus,
    string? Reason,
    bool CanSkip,
    bool CanUnskip)
{
    public string StateName => State switch
    {
        DeliveryState.Scheduled => "Scheduled",
        DeliveryState.OrderCreated => OrderStatus?.DisplayName() ?? "Order created",
        DeliveryState.Skipped => "Skipped",
        DeliveryState.Cancelled => "Cancelled",
        DeliveryState.KitchenClosed => "Kitchen closed",
        DeliveryState.Paused => "Paused",
        _ => State.ToString(),
    };
}

public sealed record StandingOrderDetailsDto
{
    public int Id { get; init; }
    public required string Reference { get; init; }
    public string? CustomerId { get; init; }
    public required string BusinessName { get; init; }
    public required string ContactName { get; init; }
    public required string Email { get; init; }
    public required string Phone { get; init; }
    public string? TaxPermitNumber { get; init; }
    public FulfillmentMethod Fulfillment { get; init; }
    public required string FulfillmentName { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? DeliveryAddress { get; init; }
    public WeekDays Days { get; init; }
    public required string DaysText { get; init; }
    public TimeOnly PreferredTime { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? Notes { get; init; }
    public string? AdminNotes { get; init; }
    public StandingOrderStatus Status { get; init; }
    public required string StatusName { get; init; }
    public string? StatusReason { get; init; }
    public bool TaxExempt { get; init; }
    public decimal DeliveryFee { get; init; }
    public decimal SubtotalPerDelivery { get; init; }
    public OrderTotals EstimatePerDelivery { get; init; } = new(0, 0, 0, 0);
    public int DeliveriesPerWeek { get; init; }
    public decimal EstimatedWeeklyTotal { get; init; }
    public DateTime CreatedAtLocal { get; init; }
    public DateTime? ApprovedAtLocal { get; init; }
    public IReadOnlyList<StandingOrderLineDto> Lines { get; init; } = [];
    public IReadOnlyList<StandingOrderEventDto> Events { get; init; } = [];
    public IReadOnlyList<UpcomingDeliveryDto> Upcoming { get; init; } = [];
    public int ChangeCutoffHours { get; init; }

    public bool IsDelivery => Fulfillment == FulfillmentMethod.Delivery;
    public bool CanPause => Status == StandingOrderStatus.Active;
    public bool CanResume => Status == StandingOrderStatus.Paused;
    public bool CanCancel => Status is StandingOrderStatus.PendingApproval or StandingOrderStatus.Active or StandingOrderStatus.Paused;
    public UpcomingDeliveryDto? NextDelivery => Upcoming.FirstOrDefault(u => u.State is DeliveryState.Scheduled or DeliveryState.OrderCreated);
}

public sealed record ProductionItemDto(int MenuItemId, string ItemName, string Unit, decimal OnlineQuantity, decimal RestaurantQuantity, decimal ProjectedQuantity)
{
    public decimal TotalQuantity => OnlineQuantity + RestaurantQuantity + ProjectedQuantity;
}

public sealed record ProductionDayDto(DateOnly Date, bool KitchenClosed, int OnlineOrders, int RestaurantDeliveries, int ProjectedDeliveries, IReadOnlyList<ProductionItemDto> Items)
{
    public decimal TotalQuantity => Items.Sum(i => i.TotalQuantity);
}

public sealed record ProductionPlanDto(DateOnly From, DateOnly To, IReadOnlyList<ProductionDayDto> Days);

public sealed record GenerationReport(int Generated, int Skipped, int Recovered, IReadOnlyList<string> Errors)
{
    public static GenerationReport Empty { get; } = new(0, 0, 0, []);
}

public sealed record StatementDto(
    StandingOrderDetailsDto StandingOrder,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<Orders.OrderSummaryDto> Orders,
    decimal Subtotal,
    decimal DeliveryFees,
    decimal Tax,
    decimal Total);
