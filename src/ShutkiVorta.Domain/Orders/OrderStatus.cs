namespace ShutkiVorta.Domain.Orders;

public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Preparing = 2,
    ReadyForPickup = 3,
    OutForDelivery = 4,
    Completed = 5,
    Cancelled = 6,
}

public static class OrderStatusExtensions
{
    public static string DisplayName(this OrderStatus status) => status switch
    {
        OrderStatus.Pending => "Pending",
        OrderStatus.Confirmed => "Confirmed",
        OrderStatus.Preparing => "Preparing",
        OrderStatus.ReadyForPickup => "Ready for pickup",
        OrderStatus.OutForDelivery => "Out for delivery",
        OrderStatus.Completed => "Completed",
        OrderStatus.Cancelled => "Cancelled",
        _ => status.ToString(),
    };

    public static string CustomerMessage(this OrderStatus status) => status switch
    {
        OrderStatus.Pending => "We have received your order and will confirm it shortly.",
        OrderStatus.Confirmed => "Your order is confirmed and scheduled in our kitchen.",
        OrderStatus.Preparing => "Our cooks are roasting, pounding and mixing your vortas the traditional way.",
        OrderStatus.ReadyForPickup => "Your order is packed and ready for pickup.",
        OrderStatus.OutForDelivery => "Your order is on its way to you.",
        OrderStatus.Completed => "Your order has been completed. We hope you enjoy every bite!",
        OrderStatus.Cancelled => "This order has been cancelled.",
        _ => string.Empty,
    };

    public static bool IsFinal(this OrderStatus status) =>
        status is OrderStatus.Completed or OrderStatus.Cancelled;

    /// <summary>Position in the fulfilment pipeline; used to only allow forward progress.</summary>
    internal static int Stage(this OrderStatus status) => status switch
    {
        OrderStatus.Pending => 0,
        OrderStatus.Confirmed => 1,
        OrderStatus.Preparing => 2,
        OrderStatus.ReadyForPickup or OrderStatus.OutForDelivery => 3,
        OrderStatus.Completed => 4,
        _ => int.MaxValue,
    };
}
