using MediatR;
using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.Application.Features.Menu;

/// <summary>Public menu (available items only), ordered by category then sort order.</summary>
public sealed record GetMenuQuery : IRequest<IReadOnlyList<MenuItemDto>>;

/// <summary>Full menu including unavailable items, for the admin panel.</summary>
public sealed record GetAdminMenuQuery : IRequest<IReadOnlyList<MenuItemDto>>, IRequireAdmin;

/// <summary>A single item by its URL slug. Unavailable items are returned too so their page can say "currently unavailable".</summary>
public sealed record GetMenuItemBySlugQuery(string Slug) : IRequest<MenuItemDto?>;

public sealed record GetMenuItemByIdQuery(int Id) : IRequest<MenuItemDto?>, IRequireAdmin;

internal sealed class MenuQueryHandlers(IMenuItemRepository repository) :
    IRequestHandler<GetMenuQuery, IReadOnlyList<MenuItemDto>>,
    IRequestHandler<GetAdminMenuQuery, IReadOnlyList<MenuItemDto>>,
    IRequestHandler<GetMenuItemBySlugQuery, MenuItemDto?>,
    IRequestHandler<GetMenuItemByIdQuery, MenuItemDto?>
{
    public async Task<IReadOnlyList<MenuItemDto>> Handle(GetMenuQuery request, CancellationToken cancellationToken) =>
        Sort(await repository.GetAllAsync(includeUnavailable: false, cancellationToken));

    public async Task<IReadOnlyList<MenuItemDto>> Handle(GetAdminMenuQuery request, CancellationToken cancellationToken) =>
        Sort(await repository.GetAllAsync(includeUnavailable: true, cancellationToken));

    public async Task<MenuItemDto?> Handle(GetMenuItemBySlugQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Slug))
        {
            return null;
        }

        var item = await repository.GetBySlugAsync(request.Slug.Trim().ToLowerInvariant(), cancellationToken);
        return item?.ToDto();
    }

    public async Task<MenuItemDto?> Handle(GetMenuItemByIdQuery request, CancellationToken cancellationToken) =>
        (await repository.GetByIdAsync(request.Id, cancellationToken))?.ToDto();

    private static IReadOnlyList<MenuItemDto> Sort(IEnumerable<Domain.Menu.MenuItem> items) =>
        items.OrderBy(i => i.Category).ThenBy(i => i.SortOrder).ThenBy(i => i.Name).Select(i => i.ToDto()).ToList();
}
