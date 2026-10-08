using MediatR;
using ShutkiVorta.Domain.Orders;

namespace ShutkiVorta.Application.Features.Orders;

public sealed record OrderPlacedNotification(string OrderNumber) : INotification;

public sealed record OrderStatusChangedNotification(
    string OrderNumber,
    OrderStatus NewStatus,
    string? Note,
    bool NotifyCustomer,
    bool ChangedByCustomer) : INotification;
