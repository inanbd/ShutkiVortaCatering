using MediatR;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Application.Features.Orders;

namespace ShutkiVorta.Application.Features.Users;

public sealed record UserListItemDto(UserAccount Account, int OrderCount);

public sealed record GetUsersQuery(string? Search, int Page = 1, int PageSize = Paging.DefaultPageSize)
    : IRequest<PagedResult<UserListItemDto>>, IRequireAdmin;

public sealed record SetUserAdminCommand(string UserId, bool IsAdmin) : IRequest<Result>, IRequireAdmin;

internal sealed class UserHandlers(IIdentityService identity, IOrderRepository orders, ICurrentUser currentUser) :
    IRequestHandler<GetUsersQuery, PagedResult<UserListItemDto>>,
    IRequestHandler<SetUserAdminCommand, Result>
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

    public async Task<Result> Handle(SetUserAdminCommand request, CancellationToken cancellationToken)
    {
        if (!request.IsAdmin && request.UserId == currentUser.UserId)
        {
            throw new ValidationException("You cannot remove your own administrator access.");
        }

        return await identity.SetAdminRoleAsync(request.UserId, request.IsAdmin);
    }
}
