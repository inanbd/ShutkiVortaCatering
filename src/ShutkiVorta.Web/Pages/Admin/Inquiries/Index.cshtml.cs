using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Features.Inquiries;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Inquiries;

public sealed class IndexModel(ISender sender) : AppPageModel(sender)
{
    [BindProperty(SupportsGet = true)] public bool All { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public PagedResult<CateringInquiryDto> Inquiries { get; private set; } = PagedResult<CateringInquiryDto>.Empty(1, 20);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Catering inquiries";
        Inquiries = await Sender.Send(new GetInquiriesQuery(!All, PageNumber), cancellationToken);
    }

    public async Task<IActionResult> OnPostHandledAsync(int id, bool isHandled, CancellationToken cancellationToken)
    {
        await Sender.Send(new SetInquiryHandledCommand(id, isHandled), cancellationToken);
        StatusMessage = isHandled ? "Inquiry marked as handled." : "Inquiry re-opened.";
        return Redirect(All ? "/admin/inquiries?all=true" : "/admin/inquiries");
    }

    public string PageUrl(int page) => $"/admin/inquiries?all={All.ToString().ToLowerInvariant()}&p={page}";
}
