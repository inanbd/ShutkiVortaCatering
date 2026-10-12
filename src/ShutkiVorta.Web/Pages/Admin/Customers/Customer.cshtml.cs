using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Customers;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Customers;

/// <summary>
/// A registered customer: contact, what they ordered, their restaurant standing orders and inquiries. Read-only; an account
/// that is not a customer (e.g. staff only) is not found.
/// </summary>
[RequirePermission(Permissions.CustomersView)]
public sealed class CustomerModel(ISender sender) : AppPageModel(sender)
{
    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    public CustomerDetailsDto Details { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var details = await Sender.Send(new GetCustomerQuery(Id), cancellationToken);
        if (details is null)
        {
            return NotFound();
        }

        Details = details;
        ViewData["Title"] = details.Customer.Name;
        return Page();
    }
}
