namespace ShutkiVorta.Domain.Orders;

public enum FulfillmentMethod
{
    Pickup = 1,
    Delivery = 2,
}

public static class FulfillmentMethodExtensions
{
    public static string DisplayName(this FulfillmentMethod method) => method switch
    {
        FulfillmentMethod.Pickup => "Pickup",
        FulfillmentMethod.Delivery => "Delivery",
        _ => method.ToString(),
    };
}
