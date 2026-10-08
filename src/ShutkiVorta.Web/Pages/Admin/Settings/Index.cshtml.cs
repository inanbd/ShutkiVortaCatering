using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Features.Emails;
using ShutkiVorta.Application.Features.Settings;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Settings;

public sealed class IndexModel(ISender sender, ICurrentUser currentUser) : AppPageModel(sender)
{
    public SettingsOverviewDto Settings { get; private set; } = null!;

    [BindProperty]
    public string? TestEmailTo { get; set; }

    /// <summary>Result of the last test, shown once after a redirect.</summary>
    [TempData]
    public string? TestResultJson { get; set; }

    public DiagnosticResult? TestResult { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Settings";
        TestEmailTo = currentUser.Email;
        Settings = await Sender.Send(new GetSettingsOverviewQuery(), cancellationToken);
        TestResult = string.IsNullOrEmpty(TestResultJson) ? null : System.Text.Json.JsonSerializer.Deserialize<DiagnosticResult>(TestResultJson);
    }

    public async Task<IActionResult> OnPostTestEmailAsync(CancellationToken cancellationToken)
    {
        var outcome = await Sender.Send(new SendTestEmailCommand(TestEmailTo ?? string.Empty), cancellationToken);
        Store(new DiagnosticResult("Send test email", outcome.Succeeded && outcome.ActuallySent, outcome.Succeeded && !outcome.ActuallySent, outcome.Message, outcome.Hint, []));
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestConnectionAsync(CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new TestSmtpConnectionCommand(), cancellationToken);
        Store(new DiagnosticResult("Test connection", result.Succeeded, false, result.Summary, result.Hint, result.Steps));
        return RedirectToPage();
    }

    private void Store(DiagnosticResult result) => TestResultJson = System.Text.Json.JsonSerializer.Serialize(result);

    public sealed record DiagnosticResult(string Action, bool Succeeded, bool Warning, string Message, string? Hint, IReadOnlyList<SmtpConnectionTestStep> Steps);
}
