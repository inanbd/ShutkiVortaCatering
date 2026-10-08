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
}

public sealed record OrderSearchCriteria
{
    public string? Search { get; init; }
    public OrderStatus? Status { get; init; }
    public FulfillmentMethod? Fulfillment { get; init; }
    public DateTime? ScheduledFrom { get; init; }
    public DateTime? ScheduledTo { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
