using ShutkiVorta.Domain.Common;

namespace ShutkiVorta.Domain.Orders;

/// <summary>A customer's catering order, for pickup in Dallas or local delivery.</summary>
public sealed class Order : Entity
{
    public const int MaxNotesLength = 1000;

    private readonly List<OrderLine> _lines = [];
    private readonly List<OrderStatusChange> _history = [];

    private Order()
    {
    }

    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>Secret used in "view your order" links so guests can see their order without an account.</summary>
    public string TrackingToken { get; private set; } = string.Empty;

    public string? CustomerId { get; private set; }
    public string CustomerName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public FulfillmentMethod Fulfillment { get; private set; }
    public string? AddressLine1 { get; private set; }
    public string? AddressLine2 { get; private set; }
    public string? City { get; private set; }
    public string? State { get; private set; }
    public string? PostalCode { get; private set; }

    /// <summary>Requested pickup/delivery time in the business' local time zone (Dallas, Central Time).</summary>
    public DateTime ScheduledFor { get; private set; }

    public string? CustomerNotes { get; private set; }
    public string? AdminNotes { get; private set; }
    public OrderStatus Status { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DeliveryFee { get; private set; }
    public decimal Tax { get; private set; }
    public decimal Total { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;
    public IReadOnlyList<OrderStatusChange> History => _history;

    public DeliveryAddress? DeliveryAddress =>
        Fulfillment == FulfillmentMethod.Delivery && AddressLine1 is not null
            ? new DeliveryAddress(AddressLine1, AddressLine2, City ?? string.Empty, State ?? string.Empty, PostalCode ?? string.Empty)
            : null;

    public bool CanBeCancelledByCustomer => Status == OrderStatus.Pending;

    public static Order Place(
        string orderNumber,
        string trackingToken,
        string? customerId,
        CustomerContact contact,
        FulfillmentMethod fulfillment,
        DeliveryAddress? address,
        DateTime scheduledFor,
        string? customerNotes,
        IEnumerable<OrderLineRequest> lines,
        OrderPricingPolicy pricing,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(pricing);

        if (!Enum.IsDefined(fulfillment))
        {
            throw new DomainException("Please choose pickup or delivery.");
        }

        var order = new Order
        {
            OrderNumber = Guard.NotEmpty(orderNumber, "Order number", 32),
            TrackingToken = Guard.NotEmpty(trackingToken, "Tracking token", 64),
            CustomerId = string.IsNullOrWhiteSpace(customerId) ? null : customerId,
            CustomerName = Guard.NotEmpty(contact.Name, "Name", 120),
            Email = Guard.NotEmpty(contact.Email, "Email", 256).ToLowerInvariant(),
            Phone = Guard.NotEmpty(contact.Phone, "Phone", 32),
            Fulfillment = fulfillment,
            ScheduledFor = scheduledFor,
            CustomerNotes = Guard.Optional(customerNotes, "Notes", MaxNotesLength),
            Status = OrderStatus.Pending,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        if (fulfillment == FulfillmentMethod.Delivery)
        {
            if (address is null)
            {
                throw new DomainException("A delivery address is required for delivery orders.");
            }

            order.AddressLine1 = Guard.NotEmpty(address.Line1, "Street address", 200);
            order.AddressLine2 = Guard.Optional(address.Line2, "Apartment / suite", 200);
            order.City = Guard.NotEmpty(address.City, "City", 100);
            order.State = Guard.NotEmpty(address.State, "State", 50);
            order.PostalCode = Guard.NotEmpty(address.PostalCode, "ZIP code", 20);
        }

        // Merge duplicate menu items into a single line.
        var merged = lines
            .GroupBy(l => l.Item.Id)
            .Select(g => new OrderLineRequest(g.First().Item, g.Sum(l => l.Quantity)))
            .ToList();

        if (merged.Count == 0)
        {
            throw new DomainException("Your order is empty.");
        }

        foreach (var request in merged)
        {
            if (!request.Item.IsAvailable)
            {
                throw new DomainException($"{request.Item.Name} is currently not available.");
            }

            var quantityError = request.Item.ValidateQuantity(request.Quantity);
            if (quantityError is not null)
            {
                throw new DomainException(quantityError);
            }

            order._lines.Add(OrderLine.From(request.Item, request.Quantity));
        }

        var totals = pricing.Calculate(order._lines.Sum(l => l.LineTotal), fulfillment);
        if (fulfillment == FulfillmentMethod.Delivery && totals.Subtotal < pricing.MinimumDeliverySubtotal)
        {
            throw new DomainException($"Delivery orders require a minimum food subtotal of ${pricing.MinimumDeliverySubtotal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}.");
        }

        order.ApplyTotals(totals);
        order._history.Add(OrderStatusChange.Record(OrderStatus.Pending, "Order placed", "Customer", nowUtc));
        return order;
    }

    /// <summary>Statuses an administrator can move this order to from its current status.</summary>
    public IReadOnlyList<OrderStatus> AllowedNextStatuses()
    {
        if (Status.IsFinal())
        {
            return [];
        }

        var currentStage = Status.Stage();
        var next = Enum.GetValues<OrderStatus>()
            .Where(s => s != OrderStatus.Cancelled && s.Stage() > currentStage && IsValidForFulfillment(s))
            .ToList();
        next.Add(OrderStatus.Cancelled);
        return next;
    }

    public void ChangeStatus(OrderStatus newStatus, string? note, string changedBy, DateTime nowUtc)
    {
        if (newStatus == Status)
        {
            throw new DomainException($"Order is already {Status.DisplayName().ToLowerInvariant()}.");
        }

        if (!AllowedNextStatuses().Contains(newStatus))
        {
            throw new DomainException(
                $"An order that is {Status.DisplayName().ToLowerInvariant()} cannot be changed to {newStatus.DisplayName().ToLowerInvariant()}.");
        }

        Status = newStatus;
        UpdatedAtUtc = nowUtc;
        _history.Add(OrderStatusChange.Record(newStatus, Guard.Optional(note, "Note", 500), Guard.NotEmpty(changedBy, "Changed by", 256), nowUtc));
    }

    public void CancelByCustomer(string? reason, DateTime nowUtc)
    {
        if (!CanBeCancelledByCustomer)
        {
            throw new DomainException("This order can no longer be cancelled online. Please call us for help.");
        }

        ChangeStatus(OrderStatus.Cancelled, string.IsNullOrWhiteSpace(reason) ? "Cancelled by customer" : reason, "Customer", nowUtc);
    }

    public void UpdateAdminNotes(string? notes, DateTime nowUtc)
    {
        AdminNotes = Guard.Optional(notes, "Admin notes", 2000);
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Rehydrates child collections when loading from storage. Used by the persistence layer only.</summary>
    public void Hydrate(IEnumerable<OrderLine> lines, IEnumerable<OrderStatusChange> history)
    {
        _lines.Clear();
        _lines.AddRange(lines);
        _history.Clear();
        _history.AddRange(history.OrderBy(h => h.ChangedAtUtc).ThenBy(h => h.Id));
    }

    private bool IsValidForFulfillment(OrderStatus status) => status switch
    {
        OrderStatus.ReadyForPickup => Fulfillment == FulfillmentMethod.Pickup,
        OrderStatus.OutForDelivery => Fulfillment == FulfillmentMethod.Delivery,
        _ => true,
    };

    private void ApplyTotals(OrderTotals totals)
    {
        Subtotal = totals.Subtotal;
        DeliveryFee = totals.DeliveryFee;
        Tax = totals.Tax;
        Total = totals.Total;
    }
}
