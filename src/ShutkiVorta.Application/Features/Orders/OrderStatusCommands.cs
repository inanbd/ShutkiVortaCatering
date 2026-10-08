using System.Security.Cryptography;
using System.Text;
using MediatR;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Domain.Common;
using ShutkiVorta.Domain.Orders;

namespace ShutkiVorta.Application.Features.Orders;

/// <summary>Admin moves an order through its lifecycle (confirmed → preparing → ready → completed, or cancelled).</summary>
public sealed record UpdateOrderStatusCommand(string OrderNumber, OrderStatus NewStatus, string? Note, bool NotifyCustomer)
    : IRequest, IRequireAdmin;

public sealed record SaveOrderAdminNotesCommand(string OrderNumber, string? Notes) : IRequest, IRequireAdmin;

/// <summary>Customer cancels their own pending order, authorised either by account ownership or the order's tracking token.</summary>
public sealed record CancelOrderByCustomerCommand(string OrderNumber, string? TrackingToken, string? Reason) : IRequest;

internal sealed class OrderStatusCommandHandlers(
    IOrderRepository orders,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IPublisher publisher) :
    IRequestHandler<UpdateOrderStatusCommand>,
    IRequestHandler<SaveOrderAdminNotesCommand>,
    IRequestHandler<CancelOrderByCustomerCommand>
{
    public async Task Handle(UpdateOrderStatusCommand request, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(request.OrderNumber, cancellationToken);
        try
        {
            order.ChangeStatus(request.NewStatus, request.Note, currentUser.Email ?? "Admin", clock.UtcNow);
        }
        catch (DomainException ex)
        {
            throw new ValidationException(nameof(UpdateOrderStatusCommand.NewStatus), ex.Message);
        }

        await orders.UpdateAsync(order, cancellationToken);
        await publisher.Publish(
            new OrderStatusChangedNotification(order.OrderNumber, order.Status, request.Note, request.NotifyCustomer, ChangedByCustomer: false),
            cancellationToken);
    }

    public async Task Handle(SaveOrderAdminNotesCommand request, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(request.OrderNumber, cancellationToken);
        try
        {
            order.UpdateAdminNotes(request.Notes, clock.UtcNow);
        }
        catch (DomainException ex)
        {
            throw new ValidationException(nameof(SaveOrderAdminNotesCommand.Notes), ex.Message);
        }

        await orders.UpdateAsync(order, cancellationToken);
    }

    public async Task Handle(CancelOrderByCustomerCommand request, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(request.OrderNumber, cancellationToken);

        var ownsOrder = currentUser.UserId is not null && order.CustomerId == currentUser.UserId;
        if (!ownsOrder && !TokensMatch(order.TrackingToken, request.TrackingToken))
        {
            throw new ForbiddenAccessException("You can only cancel your own orders.");
        }

        try
        {
            order.CancelByCustomer(request.Reason, clock.UtcNow);
        }
        catch (DomainException ex)
        {
            throw new ValidationException(ex.Message);
        }

        await orders.UpdateAsync(order, cancellationToken);
        await publisher.Publish(
            new OrderStatusChangedNotification(order.OrderNumber, order.Status, request.Reason, NotifyCustomer: true, ChangedByCustomer: true),
            cancellationToken);
    }

    internal static bool TokensMatch(string expected, string? provided) =>
        !string.IsNullOrEmpty(provided)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided));

    private async Task<Order> LoadAsync(string orderNumber, CancellationToken cancellationToken) =>
        await orders.GetByNumberAsync(orderNumber.Trim().ToUpperInvariant(), cancellationToken)
        ?? throw new NotFoundException("Order", orderNumber);
}
