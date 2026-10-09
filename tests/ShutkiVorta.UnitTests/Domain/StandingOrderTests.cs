using ShutkiVorta.Domain.Common;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.Domain.Wholesale;
using ShutkiVorta.UnitTests.TestDoubles;

namespace ShutkiVorta.UnitTests.Domain;

public sealed class StandingOrderTests
{
    private static readonly WholesaleRules Rules = new(MinimumQuantityPerItem: 5m, QuantityStep: 1m, MinimumSubtotalPerDelivery: 100m);
    private static readonly DateOnly Start = new(2026, 10, 13); // a Tuesday

    private static StandingOrderLineRequest Line(int id = 1, decimal quantity = 10m, decimal price = 21.24m) =>
        new(id, $"Vorta {id}", null, "lb", price, quantity);

    private static StandingOrder Submit(
        WeekDays days = WeekDays.Tuesday | WeekDays.Friday,
        DateOnly? end = null,
        FulfillmentMethod fulfillment = FulfillmentMethod.Delivery,
        params StandingOrderLineRequest[] lines)
    {
        var order = StandingOrder.Submit(
            "RO-2610-TEST",
            "user-1",
            "Spice Garden Bistro",
            new CustomerContact("Kamal Hossain", "Kamal@SpiceGarden.test", "(214) 555-0142"),
            "32012345678",
            fulfillment,
            fulfillment == FulfillmentMethod.Delivery ? new DeliveryAddress("2800 Routh St", null, "Dallas", "TX", "75201") : null,
            days,
            new TimeOnly(10, 0),
            Start,
            end,
            "Back door please",
            lines.Length == 0 ? [Line()] : lines,
            deliveryFee: 5m,
            Rules,
            TestData.Now);
        order.AssignId(42);
        return order;
    }

    [Fact]
    public void Submit_StartsAwaitingApproval_WithNormalisedDetails()
    {
        var order = Submit();

        Assert.Equal(StandingOrderStatus.PendingApproval, order.Status);
        Assert.Equal("kamal@spicegarden.test", order.Email);
        Assert.Equal(WeekDays.Tuesday | WeekDays.Friday, order.DaysOfWeek);
        Assert.Equal(new TimeOnly(10, 0), order.PreferredTime);
        Assert.Equal(212.40m, order.SubtotalPerDelivery);
        Assert.False(order.GeneratesOrders);
        Assert.Single(order.Events);
    }

    [Fact]
    public void Submit_MergesDuplicateItems()
    {
        var order = Submit(lines: [Line(1, 5m), Line(1, 5m), Line(2, 6m, 10.19m)]);

        Assert.Equal(2, order.Lines.Count);
        Assert.Equal(10m, order.Lines.Single(l => l.MenuItemId == 1).Quantity);
    }

    [Theory]
    [InlineData(4, "minimum")]   // below the per-item minimum
    [InlineData(5.5, "steps")]   // off the 1 lb step
    public void Submit_EnforcesWholesaleQuantities(double quantity, string expected)
    {
        var ex = Assert.Throws<DomainException>(() => Submit(lines: [Line(1, (decimal)quantity, 30m)]));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Submit_EnforcesMinimumPerDelivery()
    {
        var ex = Assert.Throws<DomainException>(() => Submit(lines: [Line(1, 5m, 10m)])); // $50 < $100
        Assert.Contains("$100.00", ex.Message);
    }

    [Fact]
    public void Submit_RequiresDaysAndValidDates()
    {
        Assert.Throws<DomainException>(() => Submit(days: WeekDays.None));
        Assert.Throws<DomainException>(() => Submit(end: Start.AddDays(-1)));
        Assert.Throws<DomainException>(() => StandingOrder.Submit(
            "RO-1", null, "Bistro", new CustomerContact("A", "a@b.c", "2145550142"), null, FulfillmentMethod.Delivery, address: null,
            WeekDays.Friday, new TimeOnly(10, 0), Start, null, null, [Line()], 0m, Rules, TestData.Now));
    }

    [Fact]
    public void OccurrencesBetween_HonoursDaysStartAndEnd()
    {
        var order = Submit(end: new DateOnly(2026, 10, 23));

        var dates = order.OccurrencesBetween(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)).ToList();

        Assert.Equal([new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 23)], dates);
        Assert.Equal(new DateTime(2026, 10, 16, 10, 0, 0), order.ScheduledFor(new DateOnly(2026, 10, 16)));
    }

    [Fact]
    public void Lifecycle_FollowsAllowedTransitions()
    {
        var order = Submit();

        Assert.Throws<DomainException>(() => order.Pause("admin", null, TestData.Now));
        order.Approve("admin", "Welcome!", TestData.Now);
        Assert.True(order.GeneratesOrders);
        Assert.NotNull(order.ApprovedAtUtc);
        Assert.Throws<DomainException>(() => order.Decline("admin", null, TestData.Now));

        order.Pause("Kamal", "Renovation", TestData.Now);
        Assert.Equal(StandingOrderStatus.Paused, order.Status);
        Assert.Equal("Renovation", order.StatusReason);
        Assert.False(order.GeneratesOrders);

        order.Resume("Kamal", TestData.Now);
        Assert.Equal(StandingOrderStatus.Active, order.Status);
        Assert.Null(order.StatusReason);

        order.Cancel("Kamal", null, TestData.Now);
        Assert.True(order.Status.IsFinal());
        Assert.Throws<DomainException>(() => order.Resume("Kamal", TestData.Now));
        Assert.Throws<DomainException>(() => order.UpdateTerms([Line()], WeekDays.Friday, new TimeOnly(9, 0), Start, null, 0m, false, Rules, "admin", TestData.Now));
        Assert.Equal(5, order.Events.Count);
    }

    [Fact]
    public void UpdateTerms_RecordsWhatChanged_AndTaxExemptionRemovesTax()
    {
        var order = Submit();
        order.Approve("admin", null, TestData.Now);

        order.UpdateTerms([Line(1, 12m, 20m)], WeekDays.Weekdays, new TimeOnly(9, 30), Start, null, 0m, taxExempt: true, Rules, "admin", TestData.Now);

        Assert.Equal(240m, order.SubtotalPerDelivery);
        Assert.Equal(WeekDays.Weekdays, order.DaysOfWeek);
        Assert.True(order.TaxExempt);
        var description = order.Events[^1].Description;
        Assert.Contains("tax exempt", description);
        Assert.Contains("delivery fee", description);
        Assert.Contains("schedule updated", description);
        Assert.Contains("items or prices updated", description);

        var totals = order.PricingPolicy(0.0825m, taxDeliveryFee: true).Calculate(order.SubtotalPerDelivery, order.Fulfillment);
        Assert.Equal(0m, totals.Tax);
        Assert.Equal(240m, totals.Total);
    }

    [Fact]
    public void KitchenPause_CanOnlyBeLiftedByTheKitchen()
    {
        var order = Submit();
        order.Approve("admin", null, TestData.Now);

        order.Pause("admin@kitchen", "Unpaid invoices", TestData.Now, byKitchen: true);
        Assert.True(order.PausedByKitchen);
        var ex = Assert.Throws<DomainException>(() => order.Resume("Kamal", TestData.Now, byRestaurant: true));
        Assert.Contains("call us", ex.Message);

        order.Resume("admin@kitchen", TestData.Now);
        Assert.Equal(StandingOrderStatus.Active, order.Status);
        Assert.False(order.PausedByKitchen);

        order.Pause("Kamal", "Holiday", TestData.Now);
        order.Resume("Kamal", TestData.Now, byRestaurant: true); // the restaurant's own pause
        Assert.Equal(StandingOrderStatus.Active, order.Status);
    }

    [Fact]
    public void UpdateTerms_ReportsWhetherGeneratedOrdersAreAffected()
    {
        var order = Submit();
        order.Approve("admin", null, TestData.Now);
        var events = order.Events.Count;

        var unchanged = order.UpdateTerms([Line()], order.DaysOfWeek, order.PreferredTime, order.StartDateOnly, order.EndDateOnly,
            order.DeliveryFee, order.TaxExempt, Rules, "admin", TestData.Now);
        Assert.False(unchanged);
        Assert.Equal(events, order.Events.Count);

        Assert.True(order.UpdateTerms([Line(1, 11m)], order.DaysOfWeek, order.PreferredTime, order.StartDateOnly, order.EndDateOnly,
            order.DeliveryFee, order.TaxExempt, Rules, "admin", TestData.Now));
    }

    [Fact]
    public void GeneratedOrder_UsesAgreedPricesAndStartsConfirmed()
    {
        var order = Submit(lines: [Line(1, 10m, 21.24m), Line(2, 6m, 10.19m)]);
        order.Approve("admin", null, TestData.Now);

        var generated = Order.CreateFromStandingOrder(
            "SV-261013-ABCD", "token", order, new DateOnly(2026, 10, 13), order.PricingPolicy(0.0825m, taxDeliveryFee: true), TestData.Now);

        Assert.Equal(OrderStatus.Confirmed, generated.Status);
        Assert.Equal(42, generated.StandingOrderId);
        Assert.Equal("Spice Garden Bistro", generated.CompanyName);
        Assert.Equal("user-1", generated.CustomerId);
        Assert.Equal(new DateTime(2026, 10, 13, 10, 0, 0), generated.ScheduledFor);
        Assert.Equal(273.54m, generated.Subtotal);           // 212.40 + 61.14 at agreed prices
        Assert.Equal(5m, generated.DeliveryFee);              // wholesale fee, no free-delivery threshold
        Assert.Equal(22.98m, generated.Tax);                  // 8.25% of 278.54
        Assert.Equal(301.52m, generated.Total);
        Assert.True(generated.CanBeWithdrawnBySchedule);
        Assert.False(generated.CanBeCancelledByCustomer);
    }

    [Fact]
    public void WeekDays_CombineAndDescribe()
    {
        Assert.Equal(WeekDays.Tuesday | WeekDays.Friday, WeekDaysExtensions.Combine([DayOfWeek.Tuesday, DayOfWeek.Friday, DayOfWeek.Friday]));
        Assert.Equal("Tue, Fri", (WeekDays.Tuesday | WeekDays.Friday).Describe());
        Assert.Equal("Weekdays (Mon–Fri)", WeekDays.Weekdays.Describe());
        Assert.Equal(7, WeekDays.EveryDay.CountDays());
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Sunday], (WeekDays.Sunday | WeekDays.Monday).ToDays());
    }
}
