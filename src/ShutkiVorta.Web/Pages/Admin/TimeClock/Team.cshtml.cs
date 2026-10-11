using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Common.Settings;
using ShutkiVorta.Application.Features.Settings;
using ShutkiVorta.Application.Features.TimeClock;
using ShutkiVorta.Domain.TimeClock;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.TimeClock;

/// <summary>
/// Everyone's shifts: who is on the clock, what needs a look, the shift list and weekly hours, with CSV exports for payroll.
/// Admins can also set the kitchen location to where they stand.
/// </summary>
[RequirePermission(Permissions.TimeClockViewAll)]
public sealed class TeamModel(ISender sender, ICurrentStaff currentStaff) : AppPageModel(sender)
{
    public const string WeeklyTab = "weekly";

    private static readonly IReadOnlyDictionary<string, ShiftFilter> Filters = new Dictionary<string, ShiftFilter>(StringComparer.OrdinalIgnoreCase)
    {
        ["review"] = ShiftFilter.NeedsReview,
        ["edited"] = ShiftFilter.Edited,
        ["open"] = ShiftFilter.Open,
        ["voided"] = ShiftFilter.Voided,
    };

    /// <summary>"weekly" for weekly hours; otherwise the shift list.</summary>
    [BindProperty(SupportsGet = true)] public string? Tab { get; set; }

    [BindProperty(SupportsGet = true, Name = "user")] public string? Person { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }

    /// <summary>review, edited, open or voided (empty: every shift).</summary>
    [BindProperty(SupportsGet = true)] public string? Show { get; set; }

    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public TeamTimeClockDto Team { get; private set; } = new();
    public ShiftListDto? Shifts { get; private set; }
    public WeeklyHoursDto? Weekly { get; private set; }
    public bool IsAdmin { get; private set; }

    public bool IsWeekly => string.Equals(Tab, WeeklyTab, StringComparison.OrdinalIgnoreCase);
    public ShiftFilter Filter => Show is not null && Filters.TryGetValue(Show, out var filter) ? filter : ShiftFilter.All;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = IsWeekly ? "Weekly hours" : "Team time clock";
        IsAdmin = (await currentStaff.GetAsync(cancellationToken)).IsAdmin;
        Team = await Sender.Send(new GetTeamTimeClockQuery(), cancellationToken);

        // Without dates: this week and last week (for the review list: every shift still to review).
        if (!Request.Query.ContainsKey("from") && !Request.Query.ContainsKey("to") && (IsWeekly || Filter == ShiftFilter.All))
        {
            From = Team.ThisWeekStart.AddDays(-7);
            To = Team.Today;
        }

        if (IsWeekly)
        {
            Weekly = await Sender.Send(new GetWeeklyHoursQuery { UserId = PersonFilter, From = From, To = To }, cancellationToken);
        }
        else
        {
            Shifts = await Sender.Send(
                new GetShiftsQuery { UserId = PersonFilter, From = From, To = To, Filter = Filter, Page = PageNumber }, cancellationToken);
        }
    }

    [RequirePermission(Permissions.TimeClockViewAll)]
    public Task<IActionResult> OnGetExportShiftsAsync(CancellationToken cancellationToken) =>
        ExportAsync(new ExportShiftsCsvQuery { UserId = PersonFilter, From = From, To = To, Filter = Filter }, cancellationToken);

    [RequirePermission(Permissions.TimeClockViewAll)]
    public Task<IActionResult> OnGetExportWeeklyAsync(CancellationToken cancellationToken) =>
        ExportAsync(new ExportWeeklyHoursCsvQuery { UserId = PersonFilter, From = From, To = To }, cancellationToken);

    /// <summary>Sets the kitchen location (Settings → Time clock) to the admin's current position, through the settings page's own command.</summary>
    [AdminOnly]
    public async Task<IActionResult> OnPostKitchenLocationAsync(string? latitude, string? longitude, string? accuracy, CancellationToken cancellationToken)
    {
        var lat = Number(latitude);
        var lon = Number(longitude);
        var precision = Number(accuracy);
        if (lat is not (>= -90 and <= 90) || lon is not (>= -180 and <= 180))
        {
            ErrorMessage = "Your location could not be read. Allow location for this site (see \"How to turn location on\") and try again.";
            return Redirect(TimeClockUi.TeamPath);
        }

        if (precision > PunchLocation.ApproximateAccuracyMeters)
        {
            ErrorMessage = $"Your location is only accurate to about {TimeClockText.Distance(precision.Value)}. Turn on Precise Location (or step outside) and try again.";
            return Redirect(TimeClockUi.TeamPath);
        }

        var page = SettingsCatalog.FindBySection(TimeClockOptions.SectionName)!;
        var settings = await Sender.Send(new GetSettingsPageQuery(page.Slug), cancellationToken);
        if (settings is null)
        {
            return NotFound();
        }

        // Every other value stays as saved; the revision check refuses a save over someone else's change.
        var values = settings.Fields
            .Where(f => f.Field.Kind is not (SettingKind.List or SettingKind.DaysOfWeek or SettingKind.Secret))
            .ToDictionary(f => f.Field.Key, f => f.Value, StringComparer.OrdinalIgnoreCase);
        values["TimeClock:KitchenLatitude"] = Math.Round(lat.Value, 6).ToString("0.######", CultureInfo.InvariantCulture);
        values["TimeClock:KitchenLongitude"] = Math.Round(lon.Value, 6).ToString("0.######", CultureInfo.InvariantCulture);

        var command = new UpdateSettingsPageCommand { Slug = page.Slug, Revision = settings.Revision, Values = values };
        if (await TryExecuteAsync(() => Sender.Send(command, cancellationToken)))
        {
            var within = precision is { } meters ? $", accurate to about {TimeClockText.Distance(meters)}" : string.Empty;
            StatusMessage = $"The kitchen location is now where you are ({values["TimeClock:KitchenLatitude"]}, {values["TimeClock:KitchenLongitude"]}{within}). " +
                            "Distances are recorded from the next clock-in.";
        }
        else
        {
            ErrorMessage = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        }

        return Redirect(TimeClockUi.TeamPath);
    }

    /// <summary>This page (or one of its exports) with the current filters, on another page of results.</summary>
    public string FilterUrl(int? page = null, string? handler = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["handler"] = handler,
            ["tab"] = IsWeekly ? WeeklyTab : null,
            ["user"] = Person,
            ["from"] = From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["to"] = To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["show"] = IsWeekly ? null : Show,
            ["p"] = page is > 1 ? page.Value.ToString(CultureInfo.InvariantCulture) : null,
        };
        return QueryHelpers.AddQueryString(TimeClockUi.TeamPath, query.Where(kv => !string.IsNullOrEmpty(kv.Value)));
    }

    public string PageUrl(int page) => FilterUrl(page: page);

    private string? PersonFilter => string.IsNullOrWhiteSpace(Person) ? null : Person;

    private async Task<IActionResult> ExportAsync(IRequest<TimeClockCsvFile> query, CancellationToken cancellationToken)
    {
        TimeClockCsvFile file;
        try
        {
            file = await Sender.Send(query, cancellationToken);
        }
        catch (ValidationException ex)
        {
            ErrorMessage = ex.Message;
            return Redirect(FilterUrl());
        }

        // Shifts include where people were: never keep a copy in a browser or proxy cache.
        Response.Headers.CacheControl = "no-store";
        return File(file.Content, TimeClockCsvFile.ContentType, file.FileName);
    }

    private static double? Number(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : null;
}
