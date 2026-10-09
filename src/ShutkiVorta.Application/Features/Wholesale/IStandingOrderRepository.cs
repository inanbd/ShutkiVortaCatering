using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Domain.Wholesale;

namespace ShutkiVorta.Application.Features.Wholesale;

public interface IStandingOrderRepository
{
    Task AddAsync(StandingOrder order, CancellationToken cancellationToken = default);
    Task UpdateAsync(StandingOrder order, CancellationToken cancellationToken = default);

    /// <summary>Saves only the new history entries (no other columns), so it cannot overwrite a concurrent change.</summary>
    Task AddNewEventsAsync(StandingOrder order, CancellationToken cancellationToken = default);
    Task<StandingOrder?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<StandingOrder?> GetByReferenceAsync(string reference, CancellationToken cancellationToken = default);
    Task<bool> ReferenceExistsAsync(string reference, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StandingOrder>> GetByStatusAsync(StandingOrderStatus status, CancellationToken cancellationToken = default);
    Task<PagedResult<StandingOrderSummaryDto>> SearchAsync(StandingOrderStatus? status, string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StandingOrderSummaryDto>> GetForCustomerAsync(string customerId, CancellationToken cancellationToken = default);
    Task<int> CountByStatusAsync(StandingOrderStatus status, CancellationToken cancellationToken = default);

    // ---- Occurrence ledger: one row per standing order per delivery date ----
    Task<IReadOnlyList<OccurrenceRecord>> GetOccurrencesAsync(int standingOrderId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Inserts a ledger row unless one already exists for that date. Returns false when another process got there first.</summary>
    Task<bool> TryAddOccurrenceAsync(int standingOrderId, DateOnly date, OccurrenceStatus status, string? reason, CancellationToken cancellationToken = default);

    Task SetOccurrenceOrderAsync(int standingOrderId, DateOnly date, int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes over a claim whose order was never linked (claimed before <paramref name="staleBeforeUtc"/>). Only one caller wins.
    /// </summary>
    Task<bool> TryReclaimOrphanAsync(int standingOrderId, DateOnly date, DateTime staleBeforeUtc, DateTime nowUtc, CancellationToken cancellationToken = default);
    Task UpdateOccurrenceAsync(int standingOrderId, DateOnly date, OccurrenceStatus status, string? reason, CancellationToken cancellationToken = default);
    Task DeleteOccurrenceAsync(int standingOrderId, DateOnly date, CancellationToken cancellationToken = default);
}

/// <summary>Ledger row joined with the generated order (if any).</summary>
public sealed class OccurrenceRecord
{
    public int StandingOrderId { get; init; }
    public DateTime OccurrenceDate { get; init; }
    public OccurrenceStatus Status { get; init; }
    public int? OrderId { get; init; }
    public string? OrderNumber { get; init; }
    public Domain.Orders.OrderStatus? OrderStatus { get; init; }
    public string? Reason { get; init; }
    public DateTime CreatedAtUtc { get; init; }

    public DateOnly Date => DateOnly.FromDateTime(OccurrenceDate);
}

/// <summary>Row for standing order lists. Populated directly by the data layer.</summary>
public sealed class StandingOrderSummaryDto
{
    public int Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string? CustomerId { get; init; }
    public string BusinessName { get; init; } = string.Empty;
    public string ContactName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public Domain.Orders.FulfillmentMethod Fulfillment { get; init; }
    public int DaysOfWeek { get; init; }
    public int PreferredTimeMinutes { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public StandingOrderStatus Status { get; init; }
    public bool TaxExempt { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public decimal SubtotalPerDelivery { get; set; }
    public string ItemsPreview { get; set; } = string.Empty;

    public WeekDays Days => (WeekDays)DaysOfWeek;
    public string DaysText => Days.Describe();
    public TimeOnly PreferredTime => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(PreferredTimeMinutes));
    public string StatusName => Status.DisplayName();
}
