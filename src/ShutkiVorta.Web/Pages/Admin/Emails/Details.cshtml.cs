using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Emails;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Emails;

[RequirePermission(Permissions.EmailsView)]
public sealed class DetailsModel(ISender sender) : AppPageModel(sender)
{
    public EmailLogEntryDto Email { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var email = await Sender.Send(new GetEmailLogEntryQuery(id), cancellationToken);
        if (email is null)
        {
            return NotFound();
        }

        Email = email;
        ViewData["Title"] = "Email";
        return Page();
    }

    [RequirePermission(Permissions.EmailsManage)]
    public async Task<IActionResult> OnPostRetryAsync(long id, CancellationToken cancellationToken)
    {
        StatusMessage = await Sender.Send(new RetryEmailCommand(id), cancellationToken)
            ? "The email will be sent again in a few seconds."
            : "That email cannot be retried.";
        return Redirect($"/admin/emails/{id}");
    }
}
