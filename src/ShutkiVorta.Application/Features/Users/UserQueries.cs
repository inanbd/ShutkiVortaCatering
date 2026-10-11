using MediatR;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Application.Features.Orders;

namespace ShutkiVorta.Application.Features.Users;

public sealed record UserListItemDto(UserAccount Account, int OrderCount);

/// <summary>Every registered account with its order count. Admins only until the customer list replaces it.</summary>
public sealed record GetUsersQuery(string? Search, int Page = 1, int PageSize = Paging.DefaultPageSize)
    : IRequest<PagedResult<UserListItemDto>>, IRequireAdmin;

internal sealed class UserHandlers(IIdentityService identity, IOrderRepository orders) :
    IRequestHandler<GetUsersQuery, PagedResult<UserListItemDto>>
{
    public async Task<PagedResult<UserListItemDto>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var (page, size) = Paging.Normalize(request.Page, request.PageSize);
        var users = await identity.SearchUsersAsync(request.Search, page, size, cancellationToken);
        var counts = await orders.CountByCustomerAsync(users.Items.Select(u => u.Id).ToArray(), cancellationToken);

        var items = users.Items
            .Select(u => new UserListItemDto(u, counts.TryGetValue(u.Id, out var c) ? c : 0))
            .ToList();
        return new PagedResult<UserListItemDto>(items, users.TotalCount, users.Page, users.PageSize);
    }
}
