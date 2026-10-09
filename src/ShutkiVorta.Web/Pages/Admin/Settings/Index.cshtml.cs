using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Common.Settings;
using ShutkiVorta.Application.Features.Emails;
using ShutkiVorta.Application.Features.Settings;
using ShutkiVorta.Web.Infrastructure;
using ShutkiVorta.Web.Services;

namespace ShutkiVorta.Web.Pages.Admin.Settings;

/// <summary>Admin → Settings: one card per settings page, anything that needs attention, and the email delivery check.</summary>
public sealed class IndexModel(
    ISender sender,
    ICurrentUser currentUser,
    IOptions<AccountOptions> accounts,
    IOptions<RateLimitingOptions> rateLimiting) : AppPageModel(sender)
{
    public SettingsOverviewDto Settings { get; private set; } = null!;

    public IReadOnlyList<SettingsCard> Cards { get; private set; } = [];

    public SettingsNoticesDto Notices { get; private set; } = new([], [], []);

    [BindProperty]
    public string? TestEmailTo { get; set; }

    /// <summary>Result of the last test, shown once after a redirect.</summary>
    [TempData]
    public string? TestResultJson { get; set; }

    public EmailDiagnosticResult? TestResult { get; private set; }

    public EmailDiagnosticsPanel EmailDiagnostics => new(Settings, TestResult, TestEmailTo, ReturnTo: null);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Settings";
        TestEmailTo = currentUser.Email;
        Settings = await Sender.Send(new GetSettingsOverviewQuery(), cancellationToken);
        var pages = await Sender.Send(new GetSettingsPagesQuery(), cancellationToken);
        Notices = await Sender.Send(new GetSettingsNoticesQuery(), cancellationToken);
        Cards = [.. pages.Select(p => new SettingsCard(p, Highlights(p.Page.Slug)))];
        TestResult = string.IsNullOrEmpty(TestResultJson) ? null : System.Text.Json.JsonSerializer.Deserialize<EmailDiagnosticResult>(TestResultJson);
    }

    /// <param name="returnTo">Settings page slug to return to (the Email page shows the same panel), or null for this page.</param>
    public async Task<IActionResult> OnPostTestEmailAsync(string? returnTo, CancellationToken cancellationToken)
    {
        var outcome = await Sender.Send(new SendTestEmailCommand(TestEmailTo ?? string.Empty), cancellationToken);
        Store(new EmailDiagnosticResult("Send test email", outcome.Succeeded && outcome.ActuallySent, outcome.Succeeded && !outcome.ActuallySent, outcome.Message, outcome.Hint, []));
        return Back(returnTo);
    }

    public async Task<IActionResult> OnPostTestConnectionAsync(string? returnTo, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new TestSmtpConnectionCommand(), cancellationToken);
        Store(new EmailDiagnosticResult("Test connection", result.Succeeded, false, result.Summary, result.Hint, result.Steps));
        return Back(returnTo);
    }

    private IActionResult Back(string? returnTo) =>
        SettingsCatalog.FindBySlug(returnTo) is { } page
            ? RedirectToPage("/Admin/Settings/Edit", null, new { slug = page.Slug }, "email-diagnostics")
            : RedirectToPage(null, null, null, "email-diagnostics");

    private void Store(EmailDiagnosticResult result) => TestResultJson = System.Text.Json.JsonSerializer.Serialize(result);

    /// <summary>A few current values for each card, so admins can see the state of things without opening every page.</summary>
    private IReadOnlyList<CardFact> Highlights(string slug)
    {
        var s = Settings;
        var c = CultureInfo.InvariantCulture;
        return slug switch
        {
            "business" =>
            [
                new(s.Business.Name),
                new(string.Join(" · ", new[] { s.Business.Phone, s.Business.Email }.Where(v => !string.IsNullOrWhiteSpace(v)))),
            ],
            "ordering" =>
            [
                s.Ordering.AcceptingOrders ? new("Accepting orders", "pill-on") : new("Orders paused", "pill-warn"),
                new($"{s.Ordering.FirstSlot}–{s.Ordering.LastSlot}, every {s.Ordering.SlotIntervalMinutes} min"),
                new($"Sales tax {(s.Ordering.TaxRate * 100m).ToString("0.###", c)}% · delivery {Ui.Money(s.Ordering.DeliveryFee)}"),
            ],
            "restaurants" =>
            [
                s.Wholesale.AcceptingRequests ? new("Accepting requests", "pill-on") : new("Requests paused", "pill-warn"),
                new($"{Ui.Number(s.Wholesale.DiscountPercent)}% wholesale discount"),
                new(s.Wholesale.AutoGenerate ? $"Orders created automatically, {s.Wholesale.GenerateDaysAhead} days ahead" : "Orders created manually"),
            ],
            "email" =>
            [
                s.EmailReport.SendsRealEmail ? new("Sending real email", "pill-on") : new("Not sending email", "pill-warn"),
                new(s.Email.AdminRecipients.Count == 0 ? "No one is notified about new orders" : $"New orders go to {string.Join(", ", s.Email.AdminRecipients)}"),
            ],
            "website" =>
            [
                s.Site.AllowSearchEngineIndexing ? new("Listed on search engines", "pill-on") : new("Hidden from search engines", "pill-warn"),
                new(string.IsNullOrWhiteSpace(s.Site.BaseUrl) ? "Web address: automatic" : s.Site.BaseUrl),
            ],
            "accounts" =>
            [
                accounts.Value.RequireConfirmedEmail ? new("Email confirmation required", "pill-on") : new("Email confirmation optional", "pill-off"),
            ],
            "spam-protection" =>
            [
                new($"{rateLimiting.Value.FormPostsPerWindow} submissions per form every {rateLimiting.Value.WindowMinutes} minutes"),
            ],
            _ => [],
        };
    }

    public sealed record SettingsCard(SettingsPageSummaryDto Summary, IReadOnlyList<CardFact> Facts)
    {
        public SettingsPage Page => Summary.Page;
    }

    /// <param name="Pill">CSS class of a status pill, or null for a plain line of text.</param>
    public sealed record CardFact(string Text, string? Pill = null);
}
