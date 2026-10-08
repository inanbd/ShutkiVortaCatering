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

        foreach (var standingOrder in active)
        {
            try
            {
                var report = await GenerateAsync(standingOrder, cancellationToken);
                generated += report.Generated;
                skipped += report.Skipped;
                recovered += report.Recovered;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Generating orders for standing order {Reference} failed", standingOrder.Reference);
                errors.Add($"{standingOrder.Reference}: {ex.Message}");
            }
        }

        if (generated + skipped + recovered > 0)
        {
            logger.LogInformation("Standing orders: generated {Generated}, skipped {Skipped}, recovered {Recovered} deliveries", generated, skipped, recovered);
        }

        return new GenerationReport(generated, skipped, recovered, errors);
    }

    public async Task<GenerationReport> GenerateAsync(StandingOrder standingOrder, CancellationToken cancellationToken = default)
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

            if (ledger.TryGetValue(date, out var existing))
            {
                // Recover a claim whose order was never written (e.g. the app stopped mid-way).
                if (existing is { Status: OccurrenceStatus.Generated, OrderId: null } && existing.CreatedAtUtc < clock.UtcNow - OrphanAfter)
                {
                    await CreateOrderAsync(standingOrder, date, cancellationToken);
                    recovered++;
                }

                continue;
            }

            if (Ordering.IsKitchenClosed(date))
            {
                if (await standingOrders.TryAddOccurrenceAsync(standingOrder.Id, date, OccurrenceStatus.Skipped, "Kitchen closed", cancellationToken))
                {
                    skipped++;
                }

                continue;
            }

            if (!await standingOrders.TryAddOccurrenceAsync(standingOrder.Id, date, OccurrenceStatus.Generated, null, cancellationToken))
            {
                continue; // Another instance claimed this date.
            }

            await CreateOrderAsync(standingOrder, date, cancellationToken);
            generated++;
        }

        return new GenerationReport(generated, skipped, recovered, []);
    }

    /// <summary>
    /// Cancels generated orders the kitchen has not started on and that are still beyond the change cut-off,
    /// marking their dates in the ledger. Returns how many were withdrawn and how many were too close to change.
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
                if (order is not null && !order.Status.IsFinal())
                {
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
    /// with the current terms. Dates the restaurant explicitly skipped stay skipped.
    /// </summary>
    public async Task ReopenWithdrawnDatesAsync(StandingOrder standingOrder, CancellationToken cancellationToken = default)
    {
        var ledger = await standingOrders.GetOccurrencesAsync(standingOrder.Id, Today, Horizon.AddDays(14), cancellationToken);
        foreach (var occurrence in ledger.Where(o => o.Status == OccurrenceStatus.Cancelled && IsChangeable(standingOrder, o.Date)))
        {
            await standingOrders.DeleteOccurrenceAsync(standingOrder.Id, occurrence.Date, cancellationToken);
        }
    }

    /// <summary>Skips one delivery date (cancelling its generated order if needed).</summary>
    public async Task SkipDateAsync(StandingOrder standingOrder, DateOnly date, string changedBy, CancellationToken cancellationToken = default)
    {
        EnsureIsDeliveryDate(standingOrder, date);
        if (!IsChangeable(standingOrder, date))
        {
            throw new DomainException($"Deliveries within {Wholesale.ChangeCutoffHours} hours can no longer be changed online. Please call us.");
        }

        var existing = (await standingOrders.GetOccurrencesAsync(standingOrder.Id, date, date, cancellationToken)).FirstOrDefault();
        var reason = $"Skipped by {changedBy}";
        if (existing is null)
        {
            if (!await standingOrders.TryAddOccurrenceAsync(standingOrder.Id, date, OccurrenceStatus.Skipped, reason, cancellationToken))
            {
                await SkipDateAsync(standingOrder, date, changedBy, cancellationToken); // Generated meanwhile; withdraw it.
            }
        }
        else if (existing.Status == OccurrenceStatus.Generated)
        {
            var (withdrawn, _) = await WithdrawUpcomingAsync(standingOrder, date, OccurrenceStatus.Skipped, reason, changedBy, cancellationToken);
            if (withdrawn == 0)
            {
                throw new DomainException("The kitchen has already started on this delivery, so it can no longer be skipped online. Please call us.");
            }
        }
        else if (existing.Status == OccurrenceStatus.Cancelled)
        {
            await standingOrders.UpdateOccurrenceAsync(standingOrder.Id, date, OccurrenceStatus.Skipped, reason, cancellationToken);
        }

        standingOrder.RecordEvent($"Delivery on {date:ddd, MMM d} skipped", changedBy, clock.UtcNow);
        await standingOrders.UpdateAsync(standingOrder, cancellationToken);
    }

    /// <summary>Restores a previously skipped delivery date; it is generated again on the next run.</summary>
    public async Task UnskipDateAsync(StandingOrder standingOrder, DateOnly date, string changedBy, CancellationToken cancellationToken = default)
    {
        EnsureIsDeliveryDate(standingOrder, date);
        if (!IsChangeable(standingOrder, date))
        {
            throw new DomainException($"Deliveries within {Wholesale.ChangeCutoffHours} hours can no longer be changed online. Please call us.");
        }

        if (Ordering.IsKitchenClosed(date))
        {
            throw new DomainException("The kitchen is closed on that date.");
        }

        var existing = (await standingOrders.GetOccurrencesAsync(standingOrder.Id, date, date, cancellationToken)).FirstOrDefault();
        if (existing is { Status: OccurrenceStatus.Skipped or OccurrenceStatus.Cancelled })
        {
            await standingOrders.DeleteOccurrenceAsync(standingOrder.Id, date, cancellationToken);
            standingOrder.RecordEvent($"Delivery on {date:ddd, MMM d} restored", changedBy, clock.UtcNow);
            await standingOrders.UpdateAsync(standingOrder, cancellationToken);
            await GenerateAsync(standingOrder, cancellationToken);
        }
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
                    OccurrenceStatus.Skipped when occurrence.Reason == "Kitchen closed" => DeliveryState.KitchenClosed,
                    OccurrenceStatus.Skipped => DeliveryState.Skipped,
                    _ => DeliveryState.Cancelled,
                };
                var orderOpen = occurrence.OrderStatus is null or OrderStatus.Pending or OrderStatus.Confirmed;
                result.Add(new UpcomingDeliveryDto(
                    date, scheduledFor, state, occurrence.OrderNumber, occurrence.OrderStatus, occurrence.Reason,
                    CanSkip: changeable && state == DeliveryState.OrderCreated && orderOpen && standingOrder.Status == StandingOrderStatus.Active,
                    CanUnskip: changeable && state is DeliveryState.Skipped or DeliveryState.Cancelled && standingOrder.Status == StandingOrderStatus.Active));
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

    private async Task CreateOrderAsync(StandingOrder standingOrder, DateOnly date, CancellationToken cancellationToken)
    {
        var number = await UniqueOrderNumberAsync(cancellationToken);
        var order = Order.CreateFromStandingOrder(
            number,
            numbers.NewTrackingToken(),
            standingOrder,
            date,
            standingOrder.PricingPolicy(Ordering.TaxRate, Ordering.TaxDeliveryFee),
            clock.UtcNow);

        await orders.AddAsync(order, cancellationToken);
        await standingOrders.SetOccurrenceOrderAsync(standingOrder.Id, date, order.Id, cancellationToken);
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

    private static void EnsureIsDeliveryDate(StandingOrder standingOrder, DateOnly date)
    {
        if (!standingOrder.OccurrencesBetween(date, date).Any())
        {
            throw new DomainException("That date is not one of this standing order's delivery days.");
        }
    }
}
