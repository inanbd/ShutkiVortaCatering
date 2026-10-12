using MediatR;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Domain.Orders;

namespace ShutkiVorta.Application.Features.Orders;

[RequirePermission(Permissions.OrdersView)]
public sealed record GetOrdersQuery : IRequest<PagedResult<OrderSummaryDto>>
{
    public string? Search { get; init; }
    public OrderStatus? Status { get; init; }
    public FulfillmentMethod? Fulfillment { get; init; }

    /// <summary>Online checkout orders or orders generated from restaurant standing orders; null for both.</summary>
    public OrderSource? Source { get; init; }
    public DateOnly? ScheduledFrom { get; init; }
    public DateOnly? ScheduledTo { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}

[RequirePermission(Permissions.OrdersView)]
public sealed record GetOrderDetailsQuery(string OrderNumber) : IRequest<OrderDetailsDto?>;

public sealed record GetMyOrdersQuery : IRequest<IReadOnlyList<OrderSummaryDto>>, IRequireAuthenticatedUser;

public sealed record GetMyOrderQuery(string OrderNumber) : IRequest<OrderDetailsDto?>, IRequireAuthenticatedUser;

/// <summary>Order status page reached from the link in confirmation emails (no account needed).</summary>
public sealed record GetOrderByTrackingTokenQuery(string OrderNumber, string TrackingToken) : IRequest<OrderDetailsDto?>;

/// <summary>
/// Public "track my order" lookup by order number + email: a read-only view of the order when they match. Never the private
/// tracking link: that link can cancel the order, and the number and email are no secret (staff who may only see orders,
/// customers or the email log see both). Changing or cancelling needs the emailed link or the customer's own account.
/// </summary>
public sealed record FindGuestOrderQuery(string OrderNumber, string Email) : IRequest<OrderDetailsDto?>;

internal sealed class OrderQueryHandlers(
    IOrderRepository orders,
    IIdentityService identity,
    ICurrentUser currentUser,
    ICurrentStaff currentStaff,
    IDateTimeProvider clock) :
    IRequestHandler<GetOrdersQuery, PagedResult<OrderSummaryDto>>,
    IRequestHandler<GetOrderDetailsQuery, OrderDetailsDto?>,
    IRequestHandler<GetMyOrdersQuery, IReadOnlyList<OrderSummaryDto>>,
    IRequestHandler<GetMyOrderQuery, OrderDetailsDto?>,
    IRequestHandler<GetOrderByTrackingTokenQuery, OrderDetailsDto?>,
    IRequestHandler<FindGuestOrderQuery, OrderDetailsDto?>
{
    public Task<PagedResult<OrderSummaryDto>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        var (page, size) = Paging.Normalize(request.Page, request.PageSize);
        return orders.SearchAsync(new OrderSearchCriteria
        {
            Search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(),
            Status = request.Status,
            Fulfillment = request.Fulfillment,
            Source = request.Source,
            ScheduledFrom = request.ScheduledFrom?.ToDateTime(TimeOnly.MinValue),
            ScheduledTo = request.ScheduledTo?.ToDateTime(TimeOnly.MaxValue),
            Page = page,
            PageSize = size,
        }, cancellationToken);
    }

    public async Task<OrderDetailsDto?> Handle(GetOrderDetailsQuery request, CancellationToken cancellationToken)
    {
        var order = (await orders.GetByNumberAsync(Normalize(request.OrderNumber), cancellationToken))?.ToDetailsDto(clock);

        // The customer's private link can cancel the order: only staff who may change orders get it.
        return order is not null && !(await currentStaff.GetAsync(cancellationToken)).Can(Permissions.OrdersManage)
            ? order with { TrackingToken = null }
            : order;
    }

    public async Task<IReadOnlyList<OrderSummaryDto>> Handle(GetMyOrdersQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId!;
        var verifiedEmail = await GetVerifiedEmailAsync(userId);
        return await orders.GetForCustomerAsync(userId, verifiedEmail, cancellationToken);
    }

    public async Task<OrderDetailsDto?> Handle(GetMyOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await orders.GetByNumberAsync(Normalize(request.OrderNumber), cancellationToken);
        if (order is null)
        {
            return null;
        }

        var userId = currentUser.UserId!;
        if (order.CustomerId == userId)
        {
            return order.ToDetailsDto(clock);
        }

        // Guest orders placed with the customer's verified email address also belong to them.
        var verifiedEmail = await GetVerifiedEmailAsync(userId);
        return order.CustomerId is null && verifiedEmail is not null && string.Equals(order.Email, verifiedEmail, StringComparison.OrdinalIgnoreCase)
            ? order.ToDetailsDto(clock)
            : null;
    }

    public async Task<OrderDetailsDto?> Handle(GetOrderByTrackingTokenQuery request, CancellationToken cancellationToken)
    {
        var order = await orders.GetByNumberAsync(Normalize(request.OrderNumber), cancellationToken);
        return order is not null && OrderStatusCommandHandlers.TokensMatch(order.TrackingToken, request.TrackingToken)
            ? order.ToDetailsDto(clock)
            : null;
    }

    public async Task<OrderDetailsDto?> Handle(FindGuestOrderQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OrderNumber) || string.IsNullOrWhiteSpace(request.Email))
        {
            return null;
        }

        var order = await orders.GetByNumberAsync(Normalize(request.OrderNumber), cancellationToken);
        return order is not null && string.Equals(order.Email, request.Email.Trim(), StringComparison.OrdinalIgnoreCase)
            ? order.ToDetailsDto(clock) with { TrackingToken = null, CanCustomerCancel = false }
            : null;
    }

    private async Task<string?> GetVerifiedEmailAsync(string userId)
    {
        var account = await identity.FindByIdAsync(userId);
        return account is { EmailConfirmed: true } ? account.Email : null;
    }

    private static string Normalize(string orderNumber) => (orderNumber ?? string.Empty).Trim().ToUpperInvariant();
}
