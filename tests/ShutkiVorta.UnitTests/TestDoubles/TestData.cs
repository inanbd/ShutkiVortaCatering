using ShutkiVorta.Domain.Menu;
using ShutkiVorta.Domain.Orders;

namespace ShutkiVorta.UnitTests.TestDoubles;

internal static class TestData
{
    public static readonly DateTime Now = new(2026, 10, 8, 15, 0, 0, DateTimeKind.Utc);

    public static readonly OrderPricingPolicy Pricing = new(DeliveryFee: 10m, FreeDeliveryThreshold: 150m, TaxRate: 0.0825m, MinimumDeliverySubtotal: 40m);

    public static MenuItem Item(int id, string name = "Loitta Shutki Vorta", decimal price = 24.99m, bool available = true)
    {
        var item = MenuItem.Create(new MenuItemDetails
        {
            Name = name,
            BengaliName = "লইট্টা শুঁটকি ভর্তা",
            Category = MenuCategory.ShutkiVorta,
            ShortDescription = "Short",
            Description = "Long description",
            PricePerUnit = price,
            MinimumQuantity = 0.5m,
            QuantityStep = 0.5m,
            SpiceLevel = 4,
            IsAvailable = available,
        }, Now);
        item.AssignId(id);
        return item;
    }

    public static Order PlaceOrder(
        FulfillmentMethod fulfillment = FulfillmentMethod.Pickup,
        params OrderLineRequest[] lines)
    {
        if (lines.Length == 0)
        {
            lines = [new OrderLineRequest(Item(1), 2m)];
        }

        return Order.Place(
            "SV-261008-ABCD",
            "token-123",
            customerId: null,
            new CustomerContact("Rahima Akter", "Rahima@Example.com", "(214) 555-0199"),
            fulfillment,
            fulfillment == FulfillmentMethod.Delivery ? new DeliveryAddress("1500 Marilla St", null, "Dallas", "TX", "75201") : null,
            new DateTime(2026, 10, 10, 12, 0, 0),
            "Extra spicy",
            lines,
            Pricing,
            Now);
    }
}
