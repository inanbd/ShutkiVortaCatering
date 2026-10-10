using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Net.Http.Headers;
using ShutkiVorta.Application.Features.Inventory;

namespace ShutkiVorta.Web.Pages.Admin.Inventory;

/// <summary>Streams a receipt photo from private storage. Admin only, like every page under /admin.</summary>
public sealed class ReceiptModel(ISender sender) : PageModel
{
    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new GetInventoryReceiptFileQuery(id), cancellationToken);
        if (file is null)
        {
            return NotFound();
        }

        // A receipt never changes once uploaded; keep it in the admin's browser cache only.
        Response.Headers.CacheControl = "private, max-age=86400";
        var disposition = new ContentDispositionHeaderValue("inline");
        if (!string.IsNullOrWhiteSpace(file.FileName))
        {
            disposition.SetHttpFileName(file.FileName);
        }

        Response.Headers.ContentDisposition = disposition.ToString();
        return File(file.Content, file.ContentType);
    }
}
