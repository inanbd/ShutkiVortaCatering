using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Customers;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Customers;

/// <summary>
/// A guest: everything ordered without an account under one email, found by one of the orders' numbers (so no email
/// appears in the address). An order that is counted on an account sends the viewer to that account's page.
/// </summary>
[RequirePermission(Permissions.CustomersView)]
public sealed class GuestModel(ISender sender) : AppPageModel(sender)
{
    [BindProperty(SupportsGet = true)]
    public string OrderNumber { get; set; } = string.Empty;

    public CustomerDetailsDto Details { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetGuestCustomerQuery(OrderNumber), cancellationToken);
        if (result?.AccountId is { } accountId)
        {
            return Redirect(CustomersUi.AccountPath(accountId));
        }

        if (result?.Details is not { } details)
        {
            return NotFound();
        }

        Details = details;
        ViewData["Title"] = details.Customer.Name;
        return Page();
    }
}
