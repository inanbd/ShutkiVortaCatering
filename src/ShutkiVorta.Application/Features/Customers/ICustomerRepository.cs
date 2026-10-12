using ShutkiVorta.Application.Features.Orders;

namespace ShutkiVorta.Application.Features.Customers;

/// <summary>
/// Who the customers are and what they ordered. Every order is counted on exactly one customer: the account that placed it,
/// else the account whose CONFIRMED email it was placed with, else the guest with that email. Accounts are customers when
/// they have the Customer role or at least one order, so staff-only accounts are not.
/// </summary>
public interface ICustomerRepository
{
    /// <summary>One page of the customer list, and how many customers of each type match the search.</summary>
    Task<CustomerListDto> SearchAsync(CustomerSearch search, CancellationToken cancellationToken = default);

    /// <summary>The account with its order figures; null when there is no such account or it is not a customer.</summary>
    Task<CustomerSummaryDto?> GetAccountAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>The guest with this exact email and their order figures; null when no guest order has it.</summary>
    Task<CustomerSummaryDto?> GetGuestAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Who the order is counted on; null when there is no such order.</summary>
    Task<OrderOwnerDto?> GetOrderOwnerAsync(string orderNumber, CancellationToken cancellationToken = default);

    /// <summary>The account's orders (its own and guest orders with its confirmed email), newest first, cancelled ones included.</summary>
    Task<IReadOnlyList<OrderSummaryDto>> GetAccountOrdersAsync(string userId, int take, CancellationToken cancellationToken = default);

    /// <summary>The guest's orders, newest first, cancelled ones included.</summary>
    Task<IReadOnlyList<OrderSummaryDto>> GetGuestOrdersAsync(string email, int take, CancellationToken cancellationToken = default);
}
