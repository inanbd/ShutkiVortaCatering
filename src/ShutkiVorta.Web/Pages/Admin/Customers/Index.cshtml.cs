using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Features.Users;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Customers;

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

    public async Task<IActionResult> OnPostAdminAsync(string userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var ok = await TryExecuteAsync(async () =>
        {
            var result = await Sender.Send(new SetUserAdminCommand(userId, isAdmin), cancellationToken);
            if (!result.Succeeded)
            {
                AddErrors(result.Errors);
            }
        });

        if (ok && ModelState.IsValid)
        {
            StatusMessage = isAdmin ? "Administrator access granted." : "Administrator access removed.";
        }
        else
        {
            ErrorMessage = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        }

        return RedirectToPage(new { q = Q });
    }

    public string PageUrl(int page) => $"/admin/customers?q={Uri.EscapeDataString(Q ?? string.Empty)}&p={page}";
}
