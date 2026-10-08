using MediatR;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShutkiVorta.Application.Features.Dashboard;

namespace ShutkiVorta.Web.Pages.Admin;

public sealed class IndexModel(ISender sender) : PageModel
{
    public DashboardDto Dashboard { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Dashboard";
        Dashboard = await sender.Send(new GetDashboardQuery(), cancellationToken);
    }
}
