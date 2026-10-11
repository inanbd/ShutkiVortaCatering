using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Users;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Customers;

/// <summary>Registered accounts with their order counts. Admins only until the customer list replaces it.</summary>
[AdminOnly]
public sealed class IndexModel(ISender sender) : AppPageModel(sender)
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public PagedResult<UserListItemDto> Users { get; private set; } = PagedResult<UserListItemDto>.Empty(1, 20);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Customers";
        Users = await Sender.Send(new GetUsersQuery(Q, PageNumber, 25), cancellationToken);
    }

    public string PageUrl(int page) => $"/admin/customers?q={Uri.EscapeDataString(Q ?? string.Empty)}&p={page}";
}
