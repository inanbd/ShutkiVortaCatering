using ShutkiVorta.Domain.Common;

namespace ShutkiVorta.Domain.Orders;

/// <summary>Audit trail entry recorded every time an order moves to a new status.</summary>
public sealed class OrderStatusChange : Entity
{
    private OrderStatusChange()
    {
    }

    public int OrderId { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? Note { get; private set; }
    public string ChangedBy { get; private set; } = string.Empty;
    public DateTime ChangedAtUtc { get; private set; }

    internal static OrderStatusChange Record(OrderStatus status, string? note, string changedBy, DateTime nowUtc) => new()
    {
        Status = status,
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        ChangedBy = changedBy,
        ChangedAtUtc = nowUtc,
    };

    public void AttachTo(int orderId) => OrderId = orderId;
}
