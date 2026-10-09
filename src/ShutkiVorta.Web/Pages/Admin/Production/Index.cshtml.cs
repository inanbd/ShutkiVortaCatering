using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Wholesale;
using ShutkiVorta.Web.Services;

namespace ShutkiVorta.Web.Pages.Admin.Production;

public sealed class IndexModel(ISender sender, IDateTimeProvider clock, IOptions<WholesaleOptions> wholesale) : PageModel
{
    public static readonly IReadOnlyList<int> PeriodOptions = [1, 3, 7, 14];

    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public int Days { get; set; } = 7;

    public ProductionPlanDto Plan { get; private set; } = null!;
    public DateOnly Today { get; private set; }
    public int GenerateDaysAhead => wholesale.Value.GenerateDaysAhead;
    public int PeriodDays => Plan.Days.Count;

    /// <summary>Quantities for the whole period, for buying and batch cooking.</summary>
    public IReadOnlyList<ProductionItemDto> PeriodItems { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Production plan";
        Today = DateOnly.FromDateTime(clock.BusinessNow);
        Plan = await sender.Send(new GetProductionPlanQuery(From ?? Today, Days), cancellationToken);
        PeriodItems = Plan.Days
            .SelectMany(d => d.Items)
            .GroupBy(i => (i.MenuItemId, i.Unit))
            .Select(g => new ProductionItemDto(
                g.Key.MenuItemId, g.First().ItemName, g.Key.Unit,
                g.Sum(i => i.OnlineQuantity), g.Sum(i => i.RestaurantQuantity), g.Sum(i => i.ProjectedQuantity)))
            .OrderByDescending(i => i.TotalQuantity)
            .ToList();
    }

    public string PeriodUrl(DateOnly from) => $"/admin/production?from={from:yyyy-MM-dd}&days={PeriodDays}";

    /// <summary>"12.5 lb" when every item uses the same unit, otherwise just the number.</summary>
    public static string TotalText(IEnumerable<ProductionItemDto> items)
    {
        var list = items.ToList();
        var units = list.Select(i => i.Unit).Distinct().ToList();
        var total = list.Sum(i => i.TotalQuantity);
        return units.Count == 1 ? Ui.Qty(total, units[0]) : Ui.Number(total);
    }
}
