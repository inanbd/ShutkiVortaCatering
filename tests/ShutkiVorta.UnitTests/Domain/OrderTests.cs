using ShutkiVorta.Domain.Common;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.UnitTests.TestDoubles;

namespace ShutkiVorta.UnitTests.Domain;

public sealed class OrderTests
{
    [Fact]
    public void Place_PickupOrder_CalculatesTotalsWithoutDeliveryFee()
    {
        var order = TestData.PlaceOrder(FulfillmentMethod.Pickup, new OrderLineRequest(TestData.Item(1, price: 24.99m), 1.5m));

        Assert.Equal(37.49m, order.Subtotal); // 37.485 rounds away from zero
        Assert.Equal(0m, order.DeliveryFee);
        Assert.Equal(3.09m, order.Tax);      // 37.49 * 8.25%
        Assert.Equal(40.58m, order.Total);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal("rahima@example.com", order.Email);
        Assert.Single(order.History);
    }

    [Fact]
    public void Place_DeliveryOrder_AddsFeeAndTaxesIt()
    {
        var order = TestData.PlaceOrder(
            FulfillmentMethod.Delivery,
            new OrderLineRequest(TestData.Item(1, price: 24.99m), 1.5m),
            new OrderLineRequest(TestData.Item(2, "Begun Vorta", 13.99m), 2m));

        Assert.Equal(65.47m, order.Subtotal);
        Assert.Equal(10m, order.DeliveryFee);
        Assert.Equal(6.23m, order.Tax);
        Assert.Equal(81.70m, order.Total);
        Assert.Equal("1500 Marilla St, Dallas, TX 75201", order.DeliveryAddress!.ToString());
    }

    [Fact]
    public void Place_DeliveryOverThreshold_IsFree()
    {
        var order = TestData.PlaceOrder(FulfillmentMethod.Delivery, new OrderLineRequest(TestData.Item(1, price: 50m), 3m));
        Assert.Equal(0m, order.DeliveryFee);
    }

    [Fact]
    public void Place_DeliveryBelowMinimum_Throws() =>
        Assert.Throws<DomainException>(() =>
            TestData.PlaceOrder(FulfillmentMethod.Delivery, new OrderLineRequest(TestData.Item(1, price: 10m), 1m)));

    [Fact]
    public void Place_DeliveryWithoutAddress_Throws() =>
        Assert.Throws<DomainException>(() => Order.Place(
            "SV-1", "t", null, new CustomerContact("A", "a@b.c", "2145550100"), FulfillmentMethod.Delivery, null,
            DateTime.Today, null, [new OrderLineRequest(TestData.Item(1), 2m)], TestData.Pricing, TestData.Now));

    [Fact]
    public void Place_MergesDuplicateItems()
    {
        var item = TestData.Item(1);
        var order = TestData.PlaceOrder(FulfillmentMethod.Pickup, new OrderLineRequest(item, 1m), new OrderLineRequest(item, 0.5m));

        var line = Assert.Single(order.Lines);
        Assert.Equal(1.5m, line.Quantity);
    }

    [Fact]
    public void Place_UnavailableItem_Throws() =>
        Assert.Throws<DomainException>(() =>
            TestData.PlaceOrder(FulfillmentMethod.Pickup, new OrderLineRequest(TestData.Item(1, available: false), 1m)));

    [Fact]
    public void Place_InvalidQuantityStep_Throws() =>
        Assert.Throws<DomainException>(() =>
            TestData.PlaceOrder(FulfillmentMethod.Pickup, new OrderLineRequest(TestData.Item(1), 1.3m)));

    [Fact]
    public void Place_EmptyOrder_Throws() =>
        Assert.Throws<DomainException>(() => Order.Place(
            "SV-1", "t", null, new CustomerContact("A", "a@b.c", "2145550100"), FulfillmentMethod.Pickup, null,
            DateTime.Today, null, [], TestData.Pricing, TestData.Now));

    [Fact]
    public void Lines_SnapshotNameAndPrice()
    {
        var order = TestData.PlaceOrder(FulfillmentMethod.Pickup, new OrderLineRequest(TestData.Item(7, "Aloo Vorta", 11.99m), 2m));
        var line = Assert.Single(order.Lines);

        Assert.Equal(7, line.MenuItemId);
        Assert.Equal("Aloo Vorta", line.ItemName);
        Assert.Equal(11.99m, line.UnitPrice);
        Assert.Equal(23.98m, line.LineTotal);
    }

    [Fact]
    public void AllowedNextStatuses_PickupOrder_NeverOffersOutForDelivery()
    {
        var order = TestData.PlaceOrder(FulfillmentMethod.Pickup);
        var next = order.AllowedNextStatuses();

        Assert.Contains(OrderStatus.Confirmed, next);
        Assert.Contains(OrderStatus.ReadyForPickup, next);
        Assert.Contains(OrderStatus.Cancelled, next);
        Assert.DoesNotContain(OrderStatus.OutForDelivery, next);
        Assert.DoesNotContain(OrderStatus.Pending, next);
    }

    [Fact]
    public void ChangeStatus_MovesForwardAndRecordsHistory()
    {
        var order = TestData.PlaceOrder();

        order.ChangeStatus(OrderStatus.Confirmed, "See you soon", "admin@example.com", TestData.Now.AddMinutes(5));
        order.ChangeStatus(OrderStatus.Preparing, null, "admin@example.com", TestData.Now.AddMinutes(10));

        Assert.Equal(OrderStatus.Preparing, order.Status);
        Assert.Equal(3, order.History.Count);
        Assert.Equal("See you soon", order.History[1].Note);
    }

    [Fact]
    public void ChangeStatus_CannotGoBackwards()
    {
        var order = TestData.PlaceOrder();
        order.ChangeStatus(OrderStatus.Preparing, null, "admin", TestData.Now);

        Assert.Throws<DomainException>(() => order.ChangeStatus(OrderStatus.Confirmed, null, "admin", TestData.Now));
    }

    [Fact]
    public void ChangeStatus_FinalOrdersAreLocked()
    {
        var order = TestData.PlaceOrder();
        order.ChangeStatus(OrderStatus.Completed, null, "admin", TestData.Now);

        Assert.Empty(order.AllowedNextStatuses());
        Assert.Throws<DomainException>(() => order.ChangeStatus(OrderStatus.Cancelled, null, "admin", TestData.Now));
    }

    [Fact]
    public void CancelByCustomer_OnlyWhilePending()
    {
        var pending = TestData.PlaceOrder();
        pending.CancelByCustomer("Plans changed", TestData.Now);
        Assert.Equal(OrderStatus.Cancelled, pending.Status);

        var confirmed = TestData.PlaceOrder();
        confirmed.ChangeStatus(OrderStatus.Confirmed, null, "admin", TestData.Now);
        Assert.Throws<DomainException>(() => confirmed.CancelByCustomer(null, TestData.Now));
    }
}
