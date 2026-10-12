using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Features.Inquiries;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Application.Features.Wholesale;

namespace ShutkiVorta.Application.Features.Customers;

/// <summary>What a customer in the list is.</summary>
public enum CustomerType
{
    /// <summary>A registered account: it has the Customer role or at least one order.</summary>
    Account = 1,

    /// <summary>Orders placed without an account, grouped by their email.</summary>
    Guest = 2,

    /// <summary>An account with a restaurant standing order (approved or not).</summary>
    Restaurant = 3,
}

public enum CustomerSort
{
    /// <summary>Most recent (non-cancelled) order first; customers without one last.</summary>
    LastOrder = 0,

    /// <summary>Highest order value first.</summary>
    OrderValue = 1,

    Name = 2,

    /// <summary>Newest account (or a guest's first order) first.</summary>
    Newest = 3,
}

public static class CustomerTypeExtensions
{
    public static string DisplayName(this CustomerType type) => type switch
    {
        CustomerType.Account => "Account",
        CustomerType.Guest => "Guest",
        CustomerType.Restaurant => "Restaurant",
        _ => type.ToString(),
    };
}

public static class CustomerSortExtensions
{
    public static string DisplayName(this CustomerSort sort) => sort switch
    {
        CustomerSort.LastOrder => "Last order",
        CustomerSort.OrderValue => "Order value",
        CustomerSort.Name => "Name",
        CustomerSort.Newest => "Newest",
        _ => sort.ToString(),
    };
}

public sealed record CustomerSearch
{
    /// <summary>Part of a name, email, phone number or restaurant name.</summary>
    public string? Search { get; init; }

    /// <summary>One type only (null: everyone).</summary>
    public CustomerType? Type { get; init; }

    public CustomerSort Sort { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}

/// <summary>
/// One row of the customer list. Orders, order value and the last order leave cancelled orders out. Populated directly by the
/// data layer; times are UTC.
/// </summary>
public sealed class CustomerListItemDto
{
    public CustomerType Type { get; init; }

    /// <summary>The account's id; null for guests.</summary>
    public string? AccountId { get; init; }

    /// <summary>The account's name, or the name on a guest's newest order.</summary>
    public string Name { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }

    /// <summary>The newest restaurant name of a restaurant's standing orders.</summary>
    public string? BusinessName { get; init; }

    public int OrderCount { get; init; }
    public decimal OrderValue { get; init; }
    public DateTime? LastOrderUtc { get; init; }

    /// <summary>When the account was created, or a guest's first order.</summary>
    public DateTime JoinedUtc { get; init; }

    /// <summary>A guest's newest order number: their page's address (no email in links).</summary>
    public string? GuestOrderNumber { get; init; }
}

public sealed class CustomerListDto
{
    public PagedResult<CustomerListItemDto> Customers { get; init; } = PagedResult<CustomerListItemDto>.Empty(1, Paging.DefaultPageSize);

    /// <summary>Customers of each type matching the search (whatever the type asked for), for the tabs.</summary>
    public int AccountCount { get; init; }

    public int GuestCount { get; init; }
    public int RestaurantCount { get; init; }

    public int TotalCount => AccountCount + GuestCount + RestaurantCount;
}

/// <summary>
/// A customer with what they ordered: the figures leave cancelled orders out. Populated by the data layer; times are UTC.
/// </summary>
public sealed class CustomerSummaryDto
{
    public CustomerType Type { get; init; }

    /// <summary>The account's id; null for guests.</summary>
    public string? AccountId { get; init; }

    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }

    /// <summary>Whether the account's email is confirmed (guests have no account).</summary>
    public bool EmailConfirmed { get; init; }

    /// <summary>The account also has a staff role (nothing more about it is shown here).</summary>
    public bool IsStaff { get; init; }

    /// <summary>When the account was created (null for guests).</summary>
    public DateTime? JoinedUtc { get; init; }

    /// <summary>The address saved on the account, on one line.</summary>
    public string? SavedAddress { get; init; }

    /// <summary>The address of the newest delivery order, on one line, and when that order was placed.</summary>
    public string? LatestDeliveryAddress { get; init; }

    public DateTime? LatestDeliveryUtc { get; init; }

    /// <summary>Every order, cancelled ones included.</summary>
    public int AllOrderCount { get; init; }

    public int OrderCount { get; init; }
    public decimal OrderValue { get; init; }
    public DateTime? FirstOrderUtc { get; init; }
    public DateTime? LastOrderUtc { get; init; }

    /// <summary>
    /// For a guest: the account that has the same email but has not confirmed it (so these orders are not counted on it),
    /// when that account is a customer.
    /// </summary>
    public string? UnconfirmedAccountId { get; init; }

    public int CancelledCount => AllOrderCount - OrderCount;
    public decimal AverageOrder => OrderCount == 0 ? 0m : Math.Round(OrderValue / OrderCount, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Who an order belongs to in the customer list.</summary>
public sealed class OrderOwnerDto
{
    /// <summary>The order's email.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// The account the order is counted on: the one that placed it, or the account whose confirmed email it was placed with.
    /// Null for a guest order.
    /// </summary>
    public string? AccountId { get; init; }
}

/// <summary>A customer's page.</summary>
public sealed class CustomerDetailsDto
{
    public CustomerSummaryDto Customer { get; init; } = new();

    /// <summary>The newest orders (at most <see cref="CustomerQueryHandlers.OrdersShown"/>), cancelled ones included.</summary>
    public IReadOnlyList<OrderSummaryDto> Orders { get; init; } = [];

    /// <summary>The account's restaurant standing orders (none for guests).</summary>
    public IReadOnlyList<StandingOrderSummaryDto> StandingOrders { get; init; } = [];

    /// <summary>Inquiries sent with the same email, newest first; null when the viewer may not see inquiries.</summary>
    public IReadOnlyList<CateringInquiryDto>? Inquiries { get; init; }

    public bool HasMoreOrders => Customer.AllOrderCount > Orders.Count;
}

/// <summary>A guest's page, or the account an order belongs to instead (the page sends the viewer there).</summary>
public sealed class GuestCustomerResult
{
    public CustomerDetailsDto? Details { get; init; }

    /// <summary>Set when the order is counted on an account rather than on a guest.</summary>
    public string? AccountId { get; init; }
}
