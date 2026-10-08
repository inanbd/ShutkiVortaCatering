using System.Globalization;
using ShutkiVorta.Domain.Common;
using ShutkiVorta.Domain.Orders;

namespace ShutkiVorta.Domain.Wholesale;

/// <summary>
/// A restaurant's recurring (standing) order: the same vortas delivered or picked up on chosen days of the week.
/// Individual <see cref="Order"/>s are generated from it a few days ahead.
/// </summary>
public sealed class StandingOrder : Entity
{
    public const int MaxNotesLength = 1000;

    private readonly List<StandingOrderLine> _lines = [];
    private readonly List<StandingOrderEvent> _events = [];

    private StandingOrder()
    {
    }

    public string Reference { get; private set; } = string.Empty;
    public string? CustomerId { get; private set; }
    public string BusinessName { get; private set; } = string.Empty;
    public string ContactName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;

    /// <summary>Texas sales tax permit number provided by the restaurant (for resale exemption).</summary>
    public string? TaxPermitNumber { get; private set; }

    public FulfillmentMethod Fulfillment { get; private set; }
    public string? AddressLine1 { get; private set; }
    public string? AddressLine2 { get; private set; }
    public string? City { get; private set; }
    public string? State { get; private set; }
    public string? PostalCode { get; private set; }

    public WeekDays DaysOfWeek { get; private set; }

    /// <summary>Delivery/pickup time as minutes after midnight (business local time).</summary>
    public int PreferredTimeMinutes { get; private set; }

    /// <summary>First date deliveries may happen (date only, business local time).</summary>
    public DateTime StartDate { get; private set; }

    public DateTime? EndDate { get; private set; }
    public string? Notes { get; private set; }
    public string? AdminNotes { get; private set; }
    public StandingOrderStatus Status { get; private set; }
    public string? StatusReason { get; private set; }

    /// <summary>Set by an administrator once a resale certificate is on file.</summary>
    public bool TaxExempt { get; private set; }

    public decimal DeliveryFee { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }

    public IReadOnlyList<StandingOrderLine> Lines => _lines;
    public IReadOnlyList<StandingOrderEvent> Events => _events;

    public TimeOnly PreferredTime => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(PreferredTimeMinutes));
    public DateOnly StartDateOnly => DateOnly.FromDateTime(StartDate);
    public DateOnly? EndDateOnly => EndDate is { } end ? DateOnly.FromDateTime(end) : null;
    public decimal SubtotalPerDelivery => _lines.Sum(l => l.LineTotal);
    public decimal QuantityPerDelivery => _lines.Sum(l => l.Quantity);

    public DeliveryAddress? DeliveryAddress =>
        Fulfillment == FulfillmentMethod.Delivery && AddressLine1 is not null
            ? new DeliveryAddress(AddressLine1, AddressLine2, City ?? string.Empty, State ?? string.Empty, PostalCode ?? string.Empty)
            : null;

    /// <summary>Whether new orders should be generated for upcoming dates.</summary>
    public bool GeneratesOrders => Status == StandingOrderStatus.Active;

    public static StandingOrder Submit(
        string reference,
        string? customerId,
        string businessName,
        CustomerContact contact,
        string? taxPermitNumber,
        FulfillmentMethod fulfillment,
        DeliveryAddress? address,
        WeekDays days,
        TimeOnly preferredTime,
        DateOnly startDate,
        DateOnly? endDate,
        string? notes,
        IEnumerable<StandingOrderLineRequest> lines,
        decimal deliveryFee,
        WholesaleRules rules,
        DateTime nowUtc)
    {
        var order = new StandingOrder
        {
            Reference = Guard.NotEmpty(reference, "Reference", 32),
            CustomerId = string.IsNullOrWhiteSpace(customerId) ? null : customerId,
            BusinessName = Guard.NotEmpty(businessName, "Restaurant name", 150),
            ContactName = Guard.NotEmpty(contact.Name, "Contact name", 120),
            Email = Guard.NotEmpty(contact.Email, "Email", 256).ToLowerInvariant(),
            Phone = Guard.NotEmpty(contact.Phone, "Phone", 32),
            TaxPermitNumber = Guard.Optional(taxPermitNumber, "Tax permit number", 32),
            Notes = Guard.Optional(notes, "Notes", MaxNotesLength),
            Status = StandingOrderStatus.PendingApproval,
            DeliveryFee = Money.Round(Math.Max(0, deliveryFee)),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        order.SetFulfillment(fulfillment, address);
        order.SetSchedule(days, preferredTime, startDate, endDate);
        order.SetLines(lines, rules);
        order._events.Add(StandingOrderEvent.Record("Standing order requested", contact.Name, nowUtc));
        return order;
    }

    /// <summary>Delivery dates (business local) between two dates inclusive, honouring days of week, start and end dates.</summary>
    public IEnumerable<DateOnly> OccurrencesBetween(DateOnly from, DateOnly to)
    {
        var start = from < StartDateOnly ? StartDateOnly : from;
        var end = EndDateOnly is { } e && e < to ? e : to;
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (DaysOfWeek.Includes(date.DayOfWeek))
            {
                yield return date;
            }
        }
    }

    public DateTime ScheduledFor(DateOnly date) => date.ToDateTime(PreferredTime);

    public void Approve(string approvedBy, string? note, DateTime nowUtc)
    {
        EnsureStatus("approved", StandingOrderStatus.PendingApproval, StandingOrderStatus.Declined);
        Status = StandingOrderStatus.Active;
        StatusReason = null;
        ApprovedAtUtc = nowUtc;
        Touch(string.IsNullOrWhiteSpace(note) ? "Approved" : $"Approved — {note.Trim()}", approvedBy, nowUtc);
    }

    public void Decline(string declinedBy, string? reason, DateTime nowUtc)
    {
        EnsureStatus("declined", StandingOrderStatus.PendingApproval);
        Status = StandingOrderStatus.Declined;
        StatusReason = Guard.Optional(reason, "Reason", 500);
        Touch(string.IsNullOrWhiteSpace(reason) ? "Declined" : $"Declined — {reason.Trim()}", declinedBy, nowUtc);
    }

    public void Pause(string pausedBy, string? reason, DateTime nowUtc)
    {
        EnsureStatus("paused", StandingOrderStatus.Active);
        Status = StandingOrderStatus.Paused;
        StatusReason = Guard.Optional(reason, "Reason", 500);
        Touch(string.IsNullOrWhiteSpace(reason) ? "Paused" : $"Paused — {reason.Trim()}", pausedBy, nowUtc);
    }

    public void Resume(string resumedBy, DateTime nowUtc)
    {
        EnsureStatus("resumed", StandingOrderStatus.Paused);
        Status = StandingOrderStatus.Active;
        StatusReason = null;
        Touch("Resumed", resumedBy, nowUtc);
    }

    public void Cancel(string cancelledBy, string? reason, DateTime nowUtc)
    {
        EnsureStatus("cancelled", StandingOrderStatus.PendingApproval, StandingOrderStatus.Active, StandingOrderStatus.Paused);
        Status = StandingOrderStatus.Cancelled;
        StatusReason = Guard.Optional(reason, "Reason", 500);
        Touch(string.IsNullOrWhiteSpace(reason) ? "Cancelled" : $"Cancelled — {reason.Trim()}", cancelledBy, nowUtc);
    }

    /// <summary>Admin changes to the agreement: items, prices, days, time, dates, fee and tax status.</summary>
    public void UpdateTerms(
        IEnumerable<StandingOrderLineRequest> lines,
        WeekDays days,
        TimeOnly preferredTime,
        DateOnly startDate,
        DateOnly? endDate,
        decimal deliveryFee,
        bool taxExempt,
        WholesaleRules rules,
        string changedBy,
        DateTime nowUtc)
    {
        if (Status.IsFinal())
        {
            throw new DomainException($"A {Status.DisplayName().ToLowerInvariant()} standing order cannot be changed.");
        }

        var changes = new List<string>();
        if (taxExempt != TaxExempt)
        {
            changes.Add(taxExempt ? "marked tax exempt (resale certificate on file)" : "tax exemption removed");
        }

        if (Money.Round(deliveryFee) != DeliveryFee)
        {
            changes.Add($"delivery fee {DeliveryFee.ToString("0.00", CultureInfo.InvariantCulture)} → {Money.Round(deliveryFee).ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        if (days != DaysOfWeek || preferredTime != PreferredTime || startDate != StartDateOnly || endDate != EndDateOnly)
        {
            changes.Add("schedule updated");
        }

        var previousLines = string.Join(";", _lines.Select(l => $"{l.MenuItemId}:{l.Quantity}:{l.UnitPrice}"));
        SetSchedule(days, preferredTime, startDate, endDate);
        SetLines(lines, rules);
        if (previousLines != string.Join(";", _lines.Select(l => $"{l.MenuItemId}:{l.Quantity}:{l.UnitPrice}")))
        {
            changes.Add("items or prices updated");
        }

        TaxExempt = taxExempt;
        DeliveryFee = Money.Round(Math.Max(0, deliveryFee));
        Touch(changes.Count == 0 ? "Terms saved (no changes)" : "Terms changed: " + string.Join(", ", changes), changedBy, nowUtc);
    }

    public void UpdateAdminNotes(string? notes, DateTime nowUtc)
    {
        AdminNotes = Guard.Optional(notes, "Admin notes", 2000);
        UpdatedAtUtc = nowUtc;
    }

    public void RecordEvent(string description, string changedBy, DateTime nowUtc) => Touch(description, changedBy, nowUtc);

    /// <summary>Pricing used for generated orders: wholesale delivery fee, no free-delivery threshold, tax unless exempt.</summary>
    public OrderPricingPolicy PricingPolicy(decimal taxRate, bool taxDeliveryFee) =>
        new(DeliveryFee, FreeDeliveryThreshold: null, TaxRate: TaxExempt ? 0m : taxRate, MinimumDeliverySubtotal: 0m, taxDeliveryFee);

    /// <summary>Rehydrates child collections when loading from storage. Used by the persistence layer only.</summary>
    public void Hydrate(IEnumerable<StandingOrderLine> lines, IEnumerable<StandingOrderEvent> events)
    {
        _lines.Clear();
        _lines.AddRange(lines);
        _events.Clear();
        _events.AddRange(events.OrderBy(e => e.ChangedAtUtc).ThenBy(e => e.Id));
    }

    private void SetFulfillment(FulfillmentMethod fulfillment, DeliveryAddress? address)
    {
        if (!Enum.IsDefined(fulfillment))
        {
            throw new DomainException("Please choose pickup or delivery.");
        }

        Fulfillment = fulfillment;
        if (fulfillment == FulfillmentMethod.Delivery)
        {
            if (address is null)
            {
                throw new DomainException("A delivery address is required for delivery.");
            }

            AddressLine1 = Guard.NotEmpty(address.Line1, "Street address", 200);
            AddressLine2 = Guard.Optional(address.Line2, "Suite", 200);
            City = Guard.NotEmpty(address.City, "City", 100);
            State = Guard.NotEmpty(address.State, "State", 50);
            PostalCode = Guard.NotEmpty(address.PostalCode, "ZIP code", 20);
        }
        else
        {
            AddressLine1 = AddressLine2 = City = State = PostalCode = null;
        }
    }

    private void SetSchedule(WeekDays days, TimeOnly preferredTime, DateOnly startDate, DateOnly? endDate)
    {
        if ((days & WeekDays.EveryDay) == WeekDays.None)
        {
            throw new DomainException("Please choose at least one day of the week.");
        }

        if (endDate is { } end && end < startDate)
        {
            throw new DomainException("The end date must be on or after the start date.");
        }

        DaysOfWeek = days & WeekDays.EveryDay;
        PreferredTimeMinutes = preferredTime.Hour * 60 + preferredTime.Minute;
        StartDate = startDate.ToDateTime(TimeOnly.MinValue);
        EndDate = endDate?.ToDateTime(TimeOnly.MinValue);
    }

    private void SetLines(IEnumerable<StandingOrderLineRequest> requests, WholesaleRules rules)
    {
        var merged = requests
            .Where(r => r.Quantity > 0)
            .GroupBy(r => r.MenuItemId)
            .Select(g => g.First() with { Quantity = g.Sum(r => r.Quantity) })
            .ToList();

        if (merged.Count == 0)
        {
            throw new DomainException("Please add at least one vorta.");
        }

        foreach (var line in merged)
        {
            if (line.Quantity < rules.MinimumQuantityPerItem)
            {
                throw new DomainException($"{line.ItemName}: the minimum for restaurant orders is {rules.MinimumQuantityPerItem:0.##} {line.Unit} per delivery.");
            }

            if (rules.QuantityStep > 0 && (line.Quantity - rules.MinimumQuantityPerItem) % rules.QuantityStep != 0)
            {
                throw new DomainException($"{line.ItemName}: please order in steps of {rules.QuantityStep:0.##} {line.Unit}.");
            }
        }

        var newLines = merged.Select(StandingOrderLine.Create).ToList();
        var subtotal = newLines.Sum(l => l.LineTotal);
        if (subtotal < rules.MinimumSubtotalPerDelivery)
        {
            throw new DomainException(
                $"Restaurant orders need at least ${rules.MinimumSubtotalPerDelivery.ToString("0.00", CultureInfo.InvariantCulture)} per delivery " +
                $"(currently ${subtotal.ToString("0.00", CultureInfo.InvariantCulture)}).");
        }

        foreach (var line in newLines)
        {
            if (!IsTransient)
            {
                line.AttachTo(Id);
            }
        }

        _lines.Clear();
        _lines.AddRange(newLines);
    }

    private void EnsureStatus(string pastTense, params StandingOrderStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainException($"A standing order that is {Status.DisplayName().ToLowerInvariant()} cannot be {pastTense}.");
        }
    }

    private void Touch(string description, string changedBy, DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        _events.Add(StandingOrderEvent.Record(description, changedBy, nowUtc));
    }
}
