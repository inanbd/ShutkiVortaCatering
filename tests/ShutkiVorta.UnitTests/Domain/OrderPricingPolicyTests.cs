using ShutkiVorta.Domain.Orders;

namespace ShutkiVorta.UnitTests.Domain;

public sealed class OrderPricingPolicyTests
{
    [Fact]
    public void Calculate_TaxesDeliveryFee_WhenConfigured()
    {
        var policy = new OrderPricingPolicy(10m, null, 0.10m, 0m, TaxDeliveryFee: true);
        var totals = policy.Calculate(100m, FulfillmentMethod.Delivery);

        Assert.Equal(new OrderTotals(100m, 10m, 11m, 121m), totals);
    }

    [Fact]
    public void Calculate_DoesNotTaxDeliveryFee_WhenDisabled()
    {
        var policy = new OrderPricingPolicy(10m, null, 0.10m, 0m, TaxDeliveryFee: false);
        var totals = policy.Calculate(100m, FulfillmentMethod.Delivery);

        Assert.Equal(10m, totals.Tax);
        Assert.Equal(120m, totals.Total);
    }

    [Fact]
    public void Calculate_EmptyDeliveryOrder_HasNoFee() =>
        Assert.Equal(0m, new OrderPricingPolicy(10m, null, 0.0825m, 0m).Calculate(0m, FulfillmentMethod.Delivery).DeliveryFee);
}
