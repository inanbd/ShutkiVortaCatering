using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Features.Wholesale;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Account.RestaurantOrders;

/// <summary>A restaurant's view of its standing order: upcoming deliveries, skip/restore dates, pause/resume/cancel.</summary>
public sealed class DetailsModel(ISender sender) : AppPageModel(sender)
{
    public StandingOrderDetailsDto StandingOrder { get; private set; } = null!;

    public bool JustSubmitted { get; private set; }

    public async Task<IActionResult> OnGetAsync(string reference, bool submitted, CancellationToken cancellationToken)
    {
        var standingOrder = await Sender.Send(new GetMyStandingOrderQuery(reference), cancellationToken);
        if (standingOrder is null)
        {
            return NotFound();
        }

        StandingOrder = standingOrder;
        JustSubmitted = submitted;
        ViewData["Title"] = $"Standing order {standingOrder.Reference}";
        return Page();
    }

    public Task<IActionResult> OnPostSkipAsync(string reference, string date, CancellationToken cancellationToken) =>
        ChangeDateAsync(reference, date, skip: true, cancellationToken);

    public Task<IActionResult> OnPostRestoreAsync(string reference, string date, CancellationToken cancellationToken) =>
        ChangeDateAsync(reference, date, skip: false, cancellationToken);

    public Task<IActionResult> OnPostPauseAsync(string reference, string? reason, CancellationToken cancellationToken) =>
        ChangeStatusAsync(reference, StandingOrderAction.Pause, reason, cancellationToken);

    public Task<IActionResult> OnPostResumeAsync(string reference, CancellationToken cancellationToken) =>
        ChangeStatusAsync(reference, StandingOrderAction.Resume, null, cancellationToken);

    public Task<IActionResult> OnPostCancelAsync(string reference, string? reason, CancellationToken cancellationToken) =>
        ChangeStatusAsync(reference, StandingOrderAction.Cancel, reason, cancellationToken);

    private async Task<IActionResult> ChangeDateAsync(string reference, string date, bool skip, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            ErrorMessage = "That date is not valid.";
            return Back(reference);
        }

        var ok = await TryExecuteAsync(() => Sender.Send(new SkipMyStandingOrderDateCommand(reference, day, skip), cancellationToken));
        if (ok)
        {
            StatusMessage = skip
                ? $"Done — no delivery on {day.ToString("dddd, MMMM d", CultureInfo.GetCultureInfo("en-US"))}."
                : $"The delivery on {day.ToString("dddd, MMMM d", CultureInfo.GetCultureInfo("en-US"))} is back on.";
        }
        else
        {
            ErrorMessage = Errors();
        }

        return Back(reference);
    }

    private async Task<IActionResult> ChangeStatusAsync(string reference, StandingOrderAction action, string? reason, CancellationToken cancellationToken)
    {
        string? message = null;
        var ok = await TryExecuteAsync(async () => message = await Sender.Send(new ChangeMyStandingOrderCommand(reference, action, reason), cancellationToken));
        if (ok)
        {
            StatusMessage = message;
        }
        else
        {
            ErrorMessage = Errors();
        }

        return Back(reference);
    }

    private string Errors() => string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));

    private RedirectResult Back(string reference) => Redirect($"/account/restaurant-orders/{Uri.EscapeDataString(reference)}");
}
