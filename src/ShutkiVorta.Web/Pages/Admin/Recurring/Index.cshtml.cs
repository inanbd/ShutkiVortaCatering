using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Wholesale;
using ShutkiVorta.Domain.Wholesale;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Recurring;

public sealed class IndexModel(ISender sender, IOptions<WholesaleOptions> wholesale) : AppPageModel(sender)
{
    public const string AllTab = "All";

    /// <summary>Status tabs in the order the kitchen works through them.</summary>
    public static readonly IReadOnlyList<StandingOrderStatus> StatusTabs =
    [
        StandingOrderStatus.PendingApproval,
        StandingOrderStatus.Active,
        StandingOrderStatus.Paused,
        StandingOrderStatus.Cancelled,
        StandingOrderStatus.Declined,
    ];

    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public PagedResult<StandingOrderSummaryDto> StandingOrders { get; private set; } = PagedResult<StandingOrderSummaryDto>.Empty(1, 20);
    public IReadOnlyDictionary<StandingOrderStatus, int> Counts { get; private set; } = new Dictionary<StandingOrderStatus, int>();

    /// <summary>The selected tab: a <see cref="StandingOrderStatus"/> name or <see cref="AllTab"/>.</summary>
    public string Tab { get; private set; } = AllTab;

    public int GenerateDaysAhead => wholesale.Value.GenerateDaysAhead;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Restaurant orders";
        Counts = await Sender.Send(new GetStandingOrderCountsQuery(), cancellationToken);
        Tab = ResolveTab();
        var status = Tab == AllTab ? (StandingOrderStatus?)null : Enum.Parse<StandingOrderStatus>(Tab);
        StandingOrders = await Sender.Send(new GetStandingOrdersQuery(status, Q, PageNumber, 20), cancellationToken);
    }

    public async Task<IActionResult> OnPostGenerateAsync(CancellationToken cancellationToken)
    {
        var report = await Sender.Send(new GenerateStandingOrderDeliveriesCommand(), cancellationToken);
        StatusMessage = report.Generated + report.Skipped + report.Recovered == 0
            ? $"Everything is up to date — no new restaurant deliveries were due in the next {GenerateDaysAhead} days."
            : $"Created {report.Generated} restaurant delivery order(s) for the next {GenerateDaysAhead} days" +
              (report.Skipped > 0 ? $", skipped {report.Skipped} date(s) when the kitchen is closed" : string.Empty) +
              (report.Recovered > 0 ? $", recovered {report.Recovered} interrupted order(s)" : string.Empty) + ".";

        if (report.Errors.Count > 0)
        {
            ErrorMessage = "Some standing orders could not be generated: " + string.Join("; ", report.Errors);
        }

        return Redirect(TabUrl(Status ?? string.Empty));
    }

    public int CountFor(string tab) =>
        tab == AllTab ? Counts.Values.Sum() : Counts.TryGetValue(Enum.Parse<StandingOrderStatus>(tab), out var count) ? count : 0;

    public string TabUrl(string tab, int page = 1, bool keepSearch = true)
    {
        var query = new Dictionary<string, string?>
        {
            ["status"] = tab,
            ["q"] = keepSearch ? Q : null,
            ["p"] = page > 1 ? page.ToString() : null,
        };
        return QueryHelpers.AddQueryString("/admin/recurring", query.Where(kv => !string.IsNullOrEmpty(kv.Value)));
    }

    public string PageUrl(int page) => TabUrl(Tab, page);

    /// <summary>An explicit tab wins; otherwise requests awaiting approval come first, then active orders.</summary>
    private string ResolveTab()
    {
        if (string.Equals(Status, AllTab, StringComparison.OrdinalIgnoreCase))
        {
            return AllTab;
        }

        if (Enum.TryParse<StandingOrderStatus>(Status, ignoreCase: true, out var status) && Enum.IsDefined(status))
        {
            return status.ToString();
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            return AllTab; // A search looks through every status.
        }

        return CountFor(nameof(StandingOrderStatus.PendingApproval)) > 0 ? nameof(StandingOrderStatus.PendingApproval)
            : CountFor(nameof(StandingOrderStatus.Active)) > 0 ? nameof(StandingOrderStatus.Active)
            : AllTab;
    }
}
