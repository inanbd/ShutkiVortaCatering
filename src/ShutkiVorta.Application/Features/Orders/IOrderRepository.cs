using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Domain.Orders;

namespace ShutkiVorta.Application.Features.Orders;

public interface IOrderRepository
{
    /// <summary>Inserts the order with its lines and status history in a single transaction.</summary>
    Task AddAsync(Order order, CancellationToken cancellationToken = default);

    Task<Order?> GetByNumberAsync(string orderNumber, CancellationToken cancellationToken = default);

    Task<bool> OrderNumberExistsAsync(string orderNumber, CancellationToken cancellationToken = default);

    /// <summary>Persists status, admin notes and any new status history entries.</summary>
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);

    Task<PagedResult<OrderSummaryDto>> SearchAsync(OrderSearchCriteria criteria, CancellationToken cancellationToken = default);

    /// <summary>Orders placed by the customer, plus (optionally) guest orders placed with their verified email address.</summary>
    Task<IReadOnlyList<OrderSummaryDto>> GetForCustomerAsync(string customerId, string? verifiedEmail, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderSummaryDto>> GetCreatedSinceAsync(DateTime fromUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderSummaryDto>> GetScheduledBetweenAsync(DateTime fromLocal, DateTime toLocal, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<OrderStatus, int>> CountByStatusAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, int>> CountByCustomerAsync(IReadOnlyCollection<string> customerIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PopularItemDto>> GetPopularItemsSinceAsync(DateTime fromUtc, int take, CancellationToken cancellationToken = default);

    /// <summary>Orders generated from one standing order, scheduled within the range (local time).</summary>
    Task<IReadOnlyList<OrderSummaryDto>> GetForStandingOrderAsync(int standingOrderId, DateTime fromLocal, DateTime toLocal, CancellationToken cancellationToken = default);

    /// <summary>Order lines of all non-cancelled orders scheduled within the range (local time), for kitchen planning.</summary>
    Task<IReadOnlyList<ProductionLine>> GetProductionLinesAsync(DateTime fromLocal, DateTime toLocal, CancellationToken cancellationToken = default);
}

/// <summary>Where an order came from.</summary>
public enum OrderSource
{
    /// <summary>Placed through the website checkout.</summary>
    Online = 1,

    /// <summary>Generated from a restaurant standing order.</summary>
    Restaurant = 2,
}

/// <summary>One order line with its order's schedule. Populated directly by the data layer.</summary>
public sealed class ProductionLine
{
    public int OrderId { get; init; }
    public int? StandingOrderId { get; init; }
    public DateTime ScheduledFor { get; init; }
    public int MenuItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
}

public sealed record OrderSearchCriteria
{
    public string? Search { get; init; }
    public OrderStatus? Status { get; init; }
    public FulfillmentMethod? Fulfillment { get; init; }
    public OrderSource? Source { get; init; }
    public DateTime? ScheduledFrom { get; init; }
    public DateTime? ScheduledTo { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
