using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Domain.Common;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.Domain.Wholesale;

namespace ShutkiVorta.Application.Features.Wholesale;

/// <summary>
/// Turns active standing orders into real <see cref="Order"/>s a few days ahead, and keeps those generated orders in step
/// when a restaurant pauses, cancels, skips a date or has its terms changed. All date maths is in Dallas local time.
/// Generation is idempotent: every (standing order, date) pair is claimed once in the occurrence ledger.
/// </summary>
public sealed class StandingOrderScheduler(
    IStandingOrderRepository standingOrders,
    IOrderRepository orders,
    IOrderNumberGenerator numbers,
    IDateTimeProvider clock,
    IOptions<WholesaleOptions> wholesale,
    IOptions<OrderingOptions> ordering,
    ILogger<StandingOrderScheduler> logger)
{
    /// <summary>Ledger reasons with a meaning of their own.</summary>
    public const string KitchenClosedReason = "Kitchen closed";
    public const string KitchenSkipReason = "Skipped by our kitchen";
    public const string TooLateReason = "Too close to the delivery to schedule online";

    private static readonly TimeSpan OrphanAfter = TimeSpan.FromMinutes(5);

    private WholesaleOptions Wholesale => wholesale.Value;
    private OrderingOptions Ordering => ordering.Value;
    private DateOnly Today => DateOnly.FromDateTime(clock.BusinessNow);
    private DateOnly Horizon => Today.AddDays(Math.Clamp(Wholesale.GenerateDaysAhead, 1, 60));

    /// <summary>Latest local time at which a delivery can still be changed online.</summary>
    public DateTime ChangeCutoff => clock.BusinessNow.AddHours(Math.Max(0, Wholesale.ChangeCutoffHours));

    public bool IsChangeable(StandingOrder standingOrder, DateOnly date) => standingOrder.ScheduledFor(date) >= ChangeCutoff;

    public async Task<GenerationReport> GenerateAllAsync(CancellationToken cancellationToken = default)
    {
        var active = await standingOrders.GetByStatusAsync(StandingOrderStatus.Active, cancellationToken);
        var generated = 0;
        var skipped = 0;
        var recovered = 0;
        var errors = new List<string>();

        foreach (var snapshot in active)
        {
            try
            {
                // Reload right before generating so a pause, cancellation or terms change made meanwhile is respected.
                var standingOrder = await standingOrders.GetByIdAsync(snapshot.Id, cancellationToken);
                if (standingOrder is null)
                {
                    continue;
                }

                var report = await GenerateAsync(standingOrder, respectCutoff: false, cancellationToken);
                generated += report.Generated;
                skipped += report.Skipped;
                recovered += report.Recovered;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Generating orders for standing order {Reference} failed", snapshot.Reference);
                errors.Add($"{snapshot.Reference}: {ex.Message}");
            }
        }

        if (generated + skipped + recovered > 0)
        {
            logger.LogInformation("Standing orders: generated {Generated}, skipped {Skipped}, recovered {Recovered} deliveries", generated, skipped, recovered);
        }

        return new GenerationReport(generated, skipped, recovered, errors);
    }

    /// <summary>
    /// Creates the orders for upcoming delivery dates that have none yet. With <paramref name="respectCutoff"/> (used for
    /// actions taken by the restaurant) dates already inside the change cut-off are not created, because the kitchen has
    /// planned without them; they are recorded as skipped instead.
    /// </summary>
    public async Task<GenerationReport> GenerateAsync(StandingOrder standingOrder, bool respectCutoff = false, CancellationToken cancellationToken = default)
    {
        if (!standingOrder.GeneratesOrders)
        {
            return GenerationReport.Empty;
        }

        var now = clock.BusinessNow;
        var ledger = (await standingOrders.GetOccurrencesAsync(standingOrder.Id, Today, Horizon, cancellationToken))
            .ToDictionary(o => o.Date);
        int generated = 0, skipped = 0, recovered = 0;

        foreach (var date in standingOrder.OccurrencesBetween(Today, Horizon))
        {
            if (standingOrder.ScheduledFor(date) <= now)
            {
                continue; // Today's slot already passed.
            }

            var closed = Ordering.IsKitchenClosed(date);
            if (ledger.TryGetValue(date, out var existing))
            {
                var changeable = IsChangeable(standingOrder, date);
                if (existing is { Status: OccurrenceStatus.Skipped, Reason: KitchenClosedReason } && !closed && changeable)
                {
                    // The closure was lifted (e.g. a holiday moved): deliver on this date after all.
                    await standingOrders.DeleteOccurrenceAsync(standingOrder.Id, date, cancellationToken);
                }
                else if (existing.Status == OccurrenceStatus.Generated && closed && changeable)
                {
                    // A closure was added after the order was generated.
                    var (withdrawn, _) = await WithdrawUpcomingAsync(standingOrder, date, OccurrenceStatus.Skipped, KitchenClosedReason, "System", cancellationToken);
                    skipped += withdrawn;
                    continue;
                }
                else
                {
                    // Recover a claim whose order was never linked (e.g. the app stopped mid-way). Only one process wins the reclaim.
                    if (existing is { Status: OccurrenceStatus.Generated, OrderId: null }
                        && await standingOrders.TryReclaimOrphanAsync(standingOrder.Id, date, clock.UtcNow - OrphanAfter, clock.UtcNow, cancellationToken))
                    {
                        await RecoverOrphanAsync(standingOrder, date, cancellationToken);
                        recovered++;
                    }

                    continue;
                }
            }

            if (closed)
            {
                if (await standingOrders.TryAddOccurrenceAsync(standingOrder.Id, date, OccurrenceStatus.Skipped, KitchenClosedReason, cancellationToken))
                {
                    skipped++;
                }

                continue;
            }

            if (respectCutoff && !IsChangeable(standingOrder, date))
            {
                if (await standingOrders.TryAddOccurrenceAsync(standingOrder.Id, date, OccurrenceStatus.Skipped, TooLateReason, cancellationToken))
                {
                    skipped++;
                }

                continue;
            }

            if (!await standingOrders.TryAddOccurrenceAsync(standingOrder.Id, date, OccurrenceStatus.Generated, null, cancellationToken))
            {
                continue; // Another instance claimed this date.
            }

            await CreateOrderAsync(standingOrder, date);
            generated++;
        }

        return new GenerationReport(generated, skipped, recovered, []);
    }

    /// <summary>
    /// Cancels generated orders the kitchen has not started on and that are still beyond the change cut-off,
    /// marking their dates in the ledger. Returns how many were withdrawn and how many were too close to change.
    /// Orders that were already cancelled or completed separately are left alone, so they are never re-created.
    /// </summary>
    public async Task<(int Withdrawn, int Locked)> WithdrawUpcomingAsync(
        StandingOrder standingOrder, DateOnly? onlyDate, OccurrenceStatus markAs, string reason, string changedBy, CancellationToken cancellationToken = default)
    {
        var from = onlyDate ?? Today;
        var to = onlyDate ?? Horizon.AddDays(14);
        var ledger = await standingOrders.GetOccurrencesAsync(standingOrder.Id, from, to, cancellationToken);
        int withdrawn = 0, locked = 0;

        foreach (var occurrence in ledger.Where(o => o.Status == OccurrenceStatus.Generated))
        {
            if (!IsChangeable(standingOrder, occurrence.Date))
            {
                locked++;
                continue;
            }

            if (occurrence.OrderNumber is not null)
            {
                var order = await orders.GetByNumberAsync(occurrence.OrderNumber, cancellationToken);
                if (order is not null)
                {
                    if (order.Status.IsFinal())
                    {
                        continue;
                    }

                    if (!order.CanBeWithdrawnBySchedule)
                    {
                        locked++;
                        continue;
                    }

                    order.ChangeStatus(OrderStatus.Cancelled, reason, changedBy, clock.UtcNow);
                    await orders.UpdateAsync(order, cancellationToken);
                }
            }

            await standingOrders.UpdateOccurrenceAsync(standingOrder.Id, occurrence.Date, markAs, reason, cancellationToken);
            withdrawn++;
        }

        return (withdrawn, locked);
    }

    /// <summary>
    /// Re-opens future dates that were withdrawn (after a resume or a change of terms) so they are generated again
    /// with the current terms. Dates that were skipped stay skipped.
    /// </summary>
    public async Task ReopenWithdrawnDatesAsync(StandingOrder standingOrder, CancellationToken cancellationToken = default)
    {
        var ledger = await standingOrders.GetOccurrencesAsync(standingOrder.Id, Today, Horizon.AddDays(14), cancellationToken);
        foreach (var occurrence in ledger.Where(o => o.Status == OccurrenceStatus.Cancelled && IsChangeable(standingOrder, o.Date)))
        {
            await standingOrders.DeleteOccurrenceAsync(standingOrder.Id, occurrence.Date, cancellationToken);
        }
    }

    /// <summary>
    /// Skips one delivery date (cancelling its generated order if needed). Returns false when it was already skipped.
    /// A date skipped by the kitchen can only be restored by the kitchen.
    /// </summary>
    public async Task<bool> SkipDateAsync(StandingOrder standingOrder, DateOnly date, string changedBy, bool byKitchen, CancellationToken cancellationToken = default)
    {
        EnsureIsDeliveryDate(standingOrder, date);
        EnsureChangeable(standingOrder, date);

        var existing = (await standingOrders.GetOccurrencesAsync(standingOrder.Id, date, date, cancellationToken)).FirstOrDefault();
        var reason = byKitchen ? KitchenSkipReason : $"Skipped by {changedBy}";
        switch (existing)
        {
            case null:
                if (!await standingOrders.TryAddOccurrenceAsync(standingOrder.Id, date, OccurrenceStatus.Skipped, reason, cancellationToken))
                {
                    return await SkipDateAsync(standingOrder, date, changedBy, byKitchen, cancellationToken); // Generated meanwhile; withdraw it.
                }

                break;
            case { Status: OccurrenceStatus.Generated, OrderStatus: OrderStatus.Cancelled }:
                await standingOrders.UpdateOccurrenceAsync(standingOrder.Id, date, OccurrenceStatus.Skipped, reason, cancellationToken);
                break;
            case { Status: OccurrenceStatus.Generated }:
                var (withdrawn, _) = await WithdrawUpcomingAsync(standingOrder, date, OccurrenceStatus.Skipped, reason, changedBy, cancellationToken);
                if (withdrawn == 0)
                {
                    throw new DomainException("The kitchen has already started on this delivery, so it can no longer be skipped online. Please call us.");
                }

                break;
            case { Status: OccurrenceStatus.Cancelled }:
                await standingOrders.UpdateOccurrenceAsync(standingOrder.Id, date, OccurrenceStatus.Skipped, reason, cancellationToken);
                break;
            default:
                return false; // Already skipped.
        }

        standingOrder.RecordEvent($"Delivery on {date:ddd, MMM d} skipped{(byKitchen ? " by the kitchen" : string.Empty)}", changedBy, clock.UtcNow);
        await standingOrders.AddNewEventsAsync(standingOrder, cancellationToken);
        return true;
    }

    /// <summary>Restores a previously skipped delivery date and generates its order. Returns false when it was not skipped.</summary>
    public async Task<bool> UnskipDateAsync(StandingOrder standingOrder, DateOnly date, string changedBy, bool byKitchen, CancellationToken cancellationToken = default)
    {
        EnsureIsDeliveryDate(standingOrder, date);
        EnsureChangeable(standingOrder, date);

        if (Ordering.IsKitchenClosed(date))
        {
            throw new DomainException("The kitchen is closed on that date.");
        }

        var existing = (await standingOrders.GetOccurrencesAsync(standingOrder.Id, date, date, cancellationToken)).FirstOrDefault();
        if (existing is not { Status: OccurrenceStatus.Skipped or OccurrenceStatus.Cancelled })
        {
            return false;
        }

        if (!byKitchen && existing.Reason == KitchenSkipReason)
        {
            throw new DomainException("Our kitchen cancelled this delivery. Please call us if you need it after all.");
        }

        await standingOrders.DeleteOccurrenceAsync(standingOrder.Id, date, cancellationToken);
        standingOrder.RecordEvent($"Delivery on {date:ddd, MMM d} restored", changedBy, clock.UtcNow);
        await standingOrders.AddNewEventsAsync(standingOrder, cancellationToken);
        await GenerateAsync(standingOrder, respectCutoff: !byKitchen, cancellationToken);
        return true;
    }

    /// <summary>The next few weeks of deliveries with their state, for the restaurant and admin views.</summary>
    public async Task<IReadOnlyList<UpcomingDeliveryDto>> GetUpcomingAsync(StandingOrder standingOrder, int days, CancellationToken cancellationToken = default)
    {
        var from = Today;
        var to = Today.AddDays(days);
        var ledger = (await standingOrders.GetOccurrencesAsync(standingOrder.Id, from, to, cancellationToken)).ToDictionary(o => o.Date);
        var now = clock.BusinessNow;
        var result = new List<UpcomingDeliveryDto>();
        var open = !standingOrder.Status.IsFinal();

        foreach (var date in standingOrder.OccurrencesBetween(from, to))
        {
            var scheduledFor = standingOrder.ScheduledFor(date);
            if (scheduledFor <= now && !ledger.ContainsKey(date))
            {
                continue;
            }

            var changeable = open && IsChangeable(standingOrder, date);
            if (ledger.TryGetValue(date, out var occurrence))
            {
                var state = occurrence.Status switch
                {
                    OccurrenceStatus.Generated => DeliveryState.OrderCreated,
                    OccurrenceStatus.Skipped when occurrence.Reason == KitchenClosedReason => DeliveryState.KitchenClosed,
                    OccurrenceStatus.Skipped => DeliveryState.Skipped,
                    _ => DeliveryState.Cancelled,
                };
                var orderOpen = occurrence.OrderStatus is null or OrderStatus.Pending or OrderStatus.Confirmed;
                result.Add(new UpcomingDeliveryDto(
                    date, scheduledFor, state, occurrence.OrderNumber, occurrence.OrderStatus, occurrence.Reason,
                    CanSkip: changeable && state == DeliveryState.OrderCreated && orderOpen && standingOrder.Status == StandingOrderStatus.Active,
                    CanUnskip: changeable && state is DeliveryState.Skipped or DeliveryState.Cancelled && standingOrder.Status == StandingOrderStatus.Active)
                {
                    SkippedByKitchen = occurrence.Reason == KitchenSkipReason,
                });
                continue;
            }

            if (Ordering.IsKitchenClosed(date))
            {
                result.Add(new UpcomingDeliveryDto(date, scheduledFor, DeliveryState.KitchenClosed, null, null, "Kitchen closed", false, false));
                continue;
            }

            var pending = standingOrder.Status switch
            {
                StandingOrderStatus.Paused => DeliveryState.Paused,
                StandingOrderStatus.Active or StandingOrderStatus.PendingApproval => DeliveryState.Scheduled,
                _ => DeliveryState.Cancelled,
            };
            result.Add(new UpcomingDeliveryDto(
                date, scheduledFor, pending, null, null, null,
                CanSkip: changeable && standingOrder.Status == StandingOrderStatus.Active,
                CanUnskip: false));
        }

        return result;
    }

    public StandingOrderDetailsDto ToDetails(StandingOrder so, IReadOnlyList<UpcomingDeliveryDto> upcoming)
    {
        var pricing = so.PricingPolicy(Ordering.TaxRate, Ordering.TaxDeliveryFee);
        var estimate = pricing.Calculate(so.SubtotalPerDelivery, so.Fulfillment);
        var perWeek = so.DaysOfWeek.CountDays();

        return new StandingOrderDetailsDto
        {
            Id = so.Id,
            Reference = so.Reference,
            CustomerId = so.CustomerId,
            BusinessName = so.BusinessName,
            ContactName = so.ContactName,
            Email = so.Email,
            Phone = so.Phone,
            TaxPermitNumber = so.TaxPermitNumber,
            Fulfillment = so.Fulfillment,
            FulfillmentName = so.Fulfillment.DisplayName(),
            AddressLine1 = so.AddressLine1,
            AddressLine2 = so.AddressLine2,
            City = so.City,
            State = so.State,
            PostalCode = so.PostalCode,
            DeliveryAddress = so.DeliveryAddress?.ToString(),
            Days = so.DaysOfWeek,
            DaysText = so.DaysOfWeek.Describe(),
            PreferredTime = so.PreferredTime,
            StartDate = so.StartDateOnly,
            EndDate = so.EndDateOnly,
            Notes = so.Notes,
            AdminNotes = so.AdminNotes,
            Status = so.Status,
            StatusName = so.Status.DisplayName(),
            StatusReason = so.StatusReason,
            TaxExempt = so.TaxExempt,
            DeliveryFee = so.DeliveryFee,
            PausedByKitchen = so.PausedByKitchen,
            SubtotalPerDelivery = so.SubtotalPerDelivery,
            EstimatePerDelivery = estimate,
            DeliveriesPerWeek = perWeek,
            EstimatedWeeklyTotal = estimate.Total * perWeek,
            CreatedAtLocal = clock.ToBusinessTime(so.CreatedAtUtc),
            ApprovedAtLocal = so.ApprovedAtUtc is { } approved ? clock.ToBusinessTime(approved) : null,
            Lines = so.Lines.Select(l => new StandingOrderLineDto(l.MenuItemId, l.ItemName, l.ItemBengaliName, l.Unit, l.UnitPrice, l.Quantity, l.LineTotal)).ToList(),
            Events = so.Events.Select(e => new StandingOrderEventDto(e.Description, e.ChangedBy, clock.ToBusinessTime(e.ChangedAtUtc))).Reverse().ToList(),
            Upcoming = upcoming,
            ChangeCutoffHours = Wholesale.ChangeCutoffHours,
        };
    }

    private async Task CreateOrderAsync(StandingOrder standingOrder, DateOnly date)
    {
        // Not cancellable: the order and its ledger link must both be written once the date is claimed.
        var number = await UniqueOrderNumberAsync(CancellationToken.None);
        var order = Order.CreateFromStandingOrder(
            number,
            numbers.NewTrackingToken(),
            standingOrder,
            date,
            standingOrder.PricingPolicy(Ordering.TaxRate, Ordering.TaxDeliveryFee),
            clock.UtcNow);

        await orders.AddAsync(order, CancellationToken.None);
        await standingOrders.SetOccurrenceOrderAsync(standingOrder.Id, date, order.Id, CancellationToken.None);
    }

    /// <summary>Links an order that was written but never linked, or creates it if it really is missing.</summary>
    private async Task RecoverOrphanAsync(StandingOrder standingOrder, DateOnly date, CancellationToken cancellationToken)
    {
        var existing = (await orders.GetForStandingOrderAsync(
                standingOrder.Id, date.ToDateTime(TimeOnly.MinValue), date.ToDateTime(TimeOnly.MaxValue), cancellationToken))
            .FirstOrDefault(o => o.Status != OrderStatus.Cancelled);

        if (existing is not null)
        {
            await standingOrders.SetOccurrenceOrderAsync(standingOrder.Id, date, existing.Id, cancellationToken);
            return;
        }

        await CreateOrderAsync(standingOrder, date);
    }

    private async Task<string> UniqueOrderNumberAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = numbers.NewOrderNumber(clock.BusinessNow);
            if (!await orders.OrderNumberExistsAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not generate a unique order number.");
    }

    private void EnsureChangeable(StandingOrder standingOrder, DateOnly date)
    {
        if (!IsChangeable(standingOrder, date))
        {
            throw new DomainException($"Deliveries within {Wholesale.ChangeCutoffHours} hours can no longer be changed online. Please call us.");
        }
    }

    private static void EnsureIsDeliveryDate(StandingOrder standingOrder, DateOnly date)
    {
        if (!standingOrder.OccurrencesBetween(date, date).Any())
        {
            throw new DomainException("That date is not one of this standing order's delivery days.");
        }
    }
}
