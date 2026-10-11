using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.TimeClock;
using ShutkiVorta.Domain.TimeClock;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.TimeClock;

/// <summary>
/// Every staff member's time clock: one big button to clock in or out (with the device's location, which the page's script
/// fills in before posting), the forgotten clock-out screen, and their shifts this week and last week.
/// </summary>
[AnyStaff]
public sealed class IndexModel(ISender sender) : AppPageModel(sender)
{
    [BindProperty]
    public PunchForm Punch { get; set; } = new();

    [BindProperty]
    public ForgottenForm Forgotten { get; set; } = new();

    public MyTimeClockDto Clock { get; private set; } = null!;

    /// <summary>Open "How to turn location on" (after a refusal for want of a location).</summary>
    public bool ShowLocationHelp { get; private set; }

    public Task OnGetAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    /// <summary>A post without a known action (an old form, a mistyped link): nothing to do but show the time clock.</summary>
    public IActionResult OnPost() => Redirect(TimeClockUi.TimeClockPath);

    public Task<IActionResult> OnPostClockInAsync(CancellationToken cancellationToken) =>
        PunchAsync(new ClockInCommand(Punch.ToLocation(), Punch.Note), cancellationToken);

    public Task<IActionResult> OnPostClockOutAsync(CancellationToken cancellationToken) =>
        PunchAsync(new ClockOutCommand(Punch.ToLocation(), Punch.Note), cancellationToken);

    public async Task<IActionResult> OnPostFixForgottenAsync(CancellationToken cancellationToken)
    {
        if (!Forgotten.DontRemember && !Forgotten.FinishingNow && Forgotten.LeftAt is null)
        {
            ModelState.AddModelError("Forgotten.LeftAt", "Enter when you left, or tap \"I don't remember\".");
        }
        else
        {
            ClockResult? result = null;
            var command = Forgotten.DontRemember
                ? new FixMyForgottenClockOutCommand(null)
                : new FixMyForgottenClockOutCommand(Forgotten.FinishingNow ? null : Forgotten.LeftAt, Forgotten.FinishingNow);
            if (await TryExecuteAsync(async () => result = await Sender.Send(command, cancellationToken)))
            {
                StatusMessage = result!.ShiftLength switch
                {
                    { } length when command.FinishingNow =>
                        $"Clocked out at {Format.Time(result.AtLocal)} after {Format.Hours(length)} · কাজ শেষ. A manager checks shifts that run past midnight.",
                    { } length => $"Thanks. Saved that you left at {Format.Time(result.AtLocal)} ({Format.Hours(length)}); a manager will check it. You can clock in now.",
                    null => "Thanks. A manager will fill in when you left. You can clock in now.",
                };
                return Redirect(TimeClockUi.TimeClockPath);
            }
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task<IActionResult> PunchAsync(IRequest<ClockResult> command, CancellationToken cancellationToken)
    {
        ClockResult? result = null;
        if (await TryExecuteAsync(async () => result = await Sender.Send(command, cancellationToken)))
        {
            StatusMessage = Describe(result!);
            return Redirect(TimeClockUi.TimeClockPath);
        }

        ShowLocationHelp = Punch.ToLocation().Status is LocationStatus.PermissionDenied or LocationStatus.Unavailable or LocationStatus.Timeout;
        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Time clock";
        Clock = await Sender.Send(new GetMyTimeClockQuery(), cancellationToken);
    }

    /// <summary>"Clocked in at 9:02 AM · কাজ শুরু. Your location was recorded (120 m from the kitchen)."</summary>
    private static string Describe(ClockResult result)
    {
        var what = result.ClockedIn ? "Clocked in" : "Clocked out";
        var bangla = result.ClockedIn ? "কাজ শুরু" : "কাজ শেষ";
        var length = !result.ClockedIn && result.ShiftLength is { } shift ? $" after {Format.Hours(shift)}" : string.Empty;
        var location = result.LocationStatus switch
        {
            LocationStatus.Captured when result.IsApproximate => "Your location was recorded, but only roughly (turn on Precise Location).",
            LocationStatus.Captured when result.DistanceMeters is { } meters => $"Your location was recorded ({TimeClockText.Distance(meters)} from the kitchen).",
            LocationStatus.Captured => "Your location was recorded.",
            var status => $"No location was recorded ({TimeClockText.Location(status).ToLower(CultureInfo.CurrentCulture)}).",
        };
        return $"{what} at {Format.Time(result.AtLocal)}{length} · {bangla}. {location}";
    }

    /// <summary>
    /// The punch form's hidden fields, filled by the page's script. Numbers are read with the invariant culture (the script
    /// sends "32.7767"); anything that isn't a known status counts as "no location sent".
    /// </summary>
    public sealed class PunchForm
    {
        public string? Status { get; set; }
        public string? Latitude { get; set; }
        public string? Longitude { get; set; }
        public string? Accuracy { get; set; }
        public string? Note { get; set; }

        public PunchLocationInput ToLocation()
        {
            var status = Enum.TryParse<LocationStatus>(Status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && parsed != LocationStatus.ManualEntry
                ? parsed
                : LocationStatus.NotProvided;
            return status == LocationStatus.Captured
                ? new PunchLocationInput(status, Number(Latitude), Number(Longitude), Number(Accuracy))
                : new PunchLocationInput(status);
        }

        private static double? Number(string? value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    public sealed class ForgottenForm
    {
        /// <summary>When the person left (business-local).</summary>
        public DateTime? LeftAt { get; set; }

        public bool DontRemember { get; set; }

        /// <summary>"I'm finishing now" on a shift that ran past midnight: it ends now, by the server's clock.</summary>
        public bool FinishingNow { get; set; }
    }
}
