using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Domain.Orders;

namespace ShutkiVorta.Application.Features.Orders;

/// <summary>Lightweight order row for lists. Populated directly by the data layer.</summary>
public sealed class OrderSummaryDto
{
    public int Id { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public string? CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public FulfillmentMethod Fulfillment { get; init; }
    public DateTime ScheduledFor { get; init; }
    public OrderStatus Status { get; init; }
    public decimal Subtotal { get; init; }
    public decimal DeliveryFee { get; init; }
    public decimal Tax { get; init; }
    public decimal Total { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public int? StandingOrderId { get; init; }
    public string? CompanyName { get; init; }
    public string ItemsPreview { get; set; } = string.Empty;

    public bool IsRestaurantOrder => StandingOrderId is not null;
    public string StatusName => Status.DisplayName();
    public string FulfillmentName => Fulfillment.DisplayName();
}

public sealed record PopularItemDto
{
    public string ItemName { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public decimal TotalQuantity { get; init; }
    public decimal Revenue { get; init; }
}

public sealed record OrderLineDto(
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

public sealed record OrderHistoryDto(OrderStatus Status, string StatusName, string? Note, string ChangedBy, DateTime ChangedAtLocal);

public sealed record OrderDetailsDto
{
    public int Id { get; init; }
    public required string OrderNumber { get; init; }
    public required string TrackingToken { get; init; }
    public string? CustomerId { get; init; }
    public required string CustomerName { get; init; }
    public required string Email { get; init; }
    public required string Phone { get; init; }
    public FulfillmentMethod Fulfillment { get; init; }
    public required string FulfillmentName { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? DeliveryAddress { get; init; }
    public DateTime ScheduledFor { get; init; }
    public string? CustomerNotes { get; init; }
    public string? AdminNotes { get; init; }
    public OrderStatus Status { get; init; }
    public required string StatusName { get; init; }
    public required string StatusMessage { get; init; }
    public decimal Subtotal { get; init; }
    public decimal DeliveryFee { get; init; }
    public decimal Tax { get; init; }
    public decimal Total { get; init; }
    public DateTime CreatedAtLocal { get; init; }
    public DateTime UpdatedAtLocal { get; init; }
    public IReadOnlyList<OrderLineDto> Lines { get; init; } = [];
    public IReadOnlyList<OrderHistoryDto> History { get; init; } = [];
    public IReadOnlyList<OrderStatus> AllowedNextStatuses { get; init; } = [];
    public bool CanCustomerCancel { get; init; }

    public bool IsDelivery => Fulfillment == FulfillmentMethod.Delivery;
    public bool IsFinal => Status.IsFinal();
}

public static class OrderMapping
{
    public static OrderDetailsDto ToDetailsDto(this Order order, IDateTimeProvider clock) => new()
    {
        Id = order.Id,
        OrderNumber = order.OrderNumber,
        TrackingToken = order.TrackingToken,
        CustomerId = order.CustomerId,
        CustomerName = order.CustomerName,
        Email = order.Email,
        Phone = order.Phone,
        Fulfillment = order.Fulfillment,
        FulfillmentName = order.Fulfillment.DisplayName(),
        AddressLine1 = order.AddressLine1,
        AddressLine2 = order.AddressLine2,
        City = order.City,
        State = order.State,
        PostalCode = order.PostalCode,
        DeliveryAddress = order.DeliveryAddress?.ToString(),
        ScheduledFor = order.ScheduledFor,
        CustomerNotes = order.CustomerNotes,
        AdminNotes = order.AdminNotes,
        Status = order.Status,
        StatusName = order.Status.DisplayName(),
        StatusMessage = order.Status.CustomerMessage(),
        Subtotal = order.Subtotal,
        DeliveryFee = order.DeliveryFee,
        Tax = order.Tax,
        Total = order.Total,
        CreatedAtLocal = clock.ToBusinessTime(order.CreatedAtUtc),
        UpdatedAtLocal = clock.ToBusinessTime(order.UpdatedAtUtc),
        Lines = order.Lines
            .Select(l => new OrderLineDto(l.MenuItemId, l.ItemName, l.ItemBengaliName, l.Unit, l.UnitPrice, l.Quantity, l.LineTotal))
            .ToList(),
        History = order.History
            .Select(h => new OrderHistoryDto(h.Status, h.Status.DisplayName(), h.Note, h.ChangedBy, clock.ToBusinessTime(h.ChangedAtUtc)))
            .ToList(),
        AllowedNextStatuses = order.AllowedNextStatuses(),
        CanCustomerCancel = order.CanBeCancelledByCustomer,
    };
}
