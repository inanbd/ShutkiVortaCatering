using MediatR;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Inquiries;
using ShutkiVorta.Application.Features.Wholesale;

namespace ShutkiVorta.Application.Features.Customers;

/// <summary>The customer list: accounts that are customers and guests grouped by email, with what they ordered.</summary>
[RequirePermission(Permissions.CustomersView)]
public sealed record GetCustomersQuery : IRequest<CustomerListDto>
{
    public string? Search { get; init; }
    public CustomerType? Type { get; init; }
    public CustomerSort Sort { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

/// <summary>A registered customer's page; null (404) when the account does not exist or is not a customer (e.g. staff only).</summary>
[RequirePermission(Permissions.CustomersView)]
public sealed record GetCustomerQuery(string UserId) : IRequest<CustomerDetailsDto?>;

/// <summary>
/// A guest's page, found by one of their order numbers. When that order is counted on an account (placed by it, or with its
/// confirmed email) the result names the account instead; null (404) when there is no such order.
/// </summary>
[RequirePermission(Permissions.CustomersView)]
public sealed record GetGuestCustomerQuery(string OrderNumber) : IRequest<GuestCustomerResult?>;

internal sealed class CustomerQueryHandlers(
    ICustomerRepository customers,
    IStandingOrderRepository standingOrders,
    ICateringInquiryRepository inquiries,
    ICurrentStaff currentStaff,
    IDateTimeProvider clock) :
    IRequestHandler<GetCustomersQuery, CustomerListDto>,
    IRequestHandler<GetCustomerQuery, CustomerDetailsDto?>,
    IRequestHandler<GetGuestCustomerQuery, GuestCustomerResult?>
{
    public const int OrdersShown = 50;
    public const int InquiriesShown = 20;

    public Task<CustomerListDto> Handle(GetCustomersQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);
        return customers.SearchAsync(new CustomerSearch
        {
            Search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(),
            Type = request.Type is { } type && Enum.IsDefined(type) ? type : null,
            Sort = Enum.IsDefined(request.Sort) ? request.Sort : CustomerSort.LastOrder,
            Page = page,
            PageSize = pageSize,
        }, cancellationToken);
    }

    public async Task<CustomerDetailsDto?> Handle(GetCustomerQuery request, CancellationToken cancellationToken)
    {
        var userId = request.UserId?.Trim() ?? string.Empty;
        var customer = userId.Length == 0 ? null : await customers.GetAccountAsync(userId, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        return new CustomerDetailsDto
        {
            Customer = customer,
            Orders = await customers.GetAccountOrdersAsync(userId, OrdersShown, cancellationToken),
            StandingOrders = await standingOrders.GetForCustomerAsync(userId, cancellationToken),
            Inquiries = await InquiriesAsync(customer.Email, cancellationToken),
        };
    }

    public async Task<GuestCustomerResult?> Handle(GetGuestCustomerQuery request, CancellationToken cancellationToken)
    {
        var orderNumber = request.OrderNumber?.Trim() ?? string.Empty;
        var owner = orderNumber.Length == 0 ? null : await customers.GetOrderOwnerAsync(orderNumber, cancellationToken);
        if (owner is null)
        {
            return null;
        }

        if (owner.AccountId is { } accountId)
        {
            return new GuestCustomerResult { AccountId = accountId };
        }

        var guest = await customers.GetGuestAsync(owner.Email, cancellationToken);
        if (guest is null)
        {
            return null;
        }

        return new GuestCustomerResult
        {
            Details = new CustomerDetailsDto
            {
                Customer = guest,
                Orders = await customers.GetGuestOrdersAsync(owner.Email, OrdersShown, cancellationToken),
                Inquiries = await InquiriesAsync(guest.Email, cancellationToken),
            },
        };
    }

    /// <summary>Inquiries sent with this email, only for staff who may see inquiries (null otherwise: not even loaded).</summary>
    private async Task<IReadOnlyList<CateringInquiryDto>?> InquiriesAsync(string email, CancellationToken cancellationToken)
    {
        if (!(await currentStaff.GetAsync(cancellationToken)).Can(Permissions.InquiriesView))
        {
            return null;
        }

        var found = await inquiries.GetByEmailAsync(email, InquiriesShown, cancellationToken);
        return found
            .Select(i => new CateringInquiryDto(i.Id, i.Topic, i.Name, i.Email, i.Phone, i.EventDate, i.GuestCount, i.Message, i.IsHandled, clock.ToBusinessTime(i.CreatedAtUtc)))
            .ToList();
    }
}
