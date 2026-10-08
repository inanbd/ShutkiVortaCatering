using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Features.Emails;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Emails;

public sealed class IndexModel(ISender sender) : AppPageModel(sender)
{
    [BindProperty(SupportsGet = true)] public EmailStatus? Status { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public PagedResult<EmailLogEntryDto> Emails { get; private set; } = PagedResult<EmailLogEntryDto>.Empty(1, 25);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Email log";
        Emails = await Sender.Send(new GetEmailLogQuery(Status, PageNumber), cancellationToken);
    }

    public async Task<IActionResult> OnPostRetryAsync(long id, CancellationToken cancellationToken)
    {
        StatusMessage = await Sender.Send(new RetryEmailCommand(id), cancellationToken)
            ? "The email will be sent again in a few seconds."
            : "That email cannot be retried.";
        return RedirectToPage(new { status = Status });
    }

    public async Task<IActionResult> OnPostRetryFailedAsync(CancellationToken cancellationToken)
    {
        var count = await Sender.Send(new RetryFailedEmailsCommand(), cancellationToken);
        StatusMessage = count == 0 ? "There are no failed emails to retry." : $"{count} failed email(s) will be sent again shortly.";
        return RedirectToPage(new { status = Status });
    }

    public string PageUrl(int page) => $"/admin/emails?status={Status}&p={page}";

    public static string StatusCss(EmailStatus status) => status switch
    {
        EmailStatus.Sent => "pill-on",
        EmailStatus.Failed => "pill-bad",
        EmailStatus.SavedToFolder or EmailStatus.Disabled => "pill-warn",
        _ => "pill-off",
    };
}
