using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Wholesale;

namespace ShutkiVorta.Web.Pages.Admin.Recurring;

public sealed class StatementModel(
    ISender sender,
    IDateTimeProvider clock,
    IOptions<BusinessOptions> business,
    IOptions<WholesaleOptions> wholesale) : PageModel
{
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }

    public StatementDto Statement { get; private set; } = null!;
    public BusinessOptions Business => business.Value;
    public string PaymentTerms => wholesale.Value.PaymentTerms;
    public DateTime PreparedAt => clock.BusinessNow;

    /// <summary>First day of the month before the statement period, for the "previous month" link.</summary>
    public DateOnly PreviousMonth => FirstOfMonth(Statement.From).AddMonths(-1);
    public DateOnly NextMonth => FirstOfMonth(Statement.From).AddMonths(1);

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        // Default to the current calendar month (business time); a lone "from" covers that month.
        var from = From ?? FirstOfMonth(DateOnly.FromDateTime(clock.BusinessNow));
        var to = To ?? FirstOfMonth(from).AddMonths(1).AddDays(-1);

        var statement = await sender.Send(new GetStandingOrderStatementQuery(id, from, to), cancellationToken);
        if (statement is null)
        {
            return NotFound();
        }

        Statement = statement;
        ViewData["Title"] = $"Statement · {statement.StandingOrder.BusinessName}";
        return Page();
    }

    public string MonthUrl(DateOnly firstOfMonth) =>
        $"/admin/recurring/{Statement.StandingOrder.Id}/statement?from={firstOfMonth:yyyy-MM-dd}&to={firstOfMonth.AddMonths(1).AddDays(-1):yyyy-MM-dd}";

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);
}
