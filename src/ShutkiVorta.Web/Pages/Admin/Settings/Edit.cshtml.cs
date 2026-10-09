using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Settings;
using ShutkiVorta.Application.Features.Settings;
using ShutkiVorta.Domain.Common;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Settings;

/// <summary>
/// Editor for one settings page (Admin → Settings → {slug}). The form is generated from <see cref="SettingsCatalog"/>: every input
/// is named by its setting key (e.g. "Email:Smtp:Port"), so values are read from <c>Request.Form</c> rather than model binding.
/// </summary>
public sealed class EditModel(ISender sender, ICurrentUser currentUser) : AppPageModel(sender)
{
    public const string EmailSlug = "email";

    public SettingsPageDto Settings { get; private set; } = null!;

    public IReadOnlyList<SettingFieldView> Fields { get; private set; } = [];

    /// <summary>The revision the form was loaded with (the posted one when the form is shown again with errors).</summary>
    public int Revision { get; private set; }

    public bool Saved { get; private set; }

    /// <summary>Errors not tied to one field are listed at the top; these are the fields with errors, in form order.</summary>
    public IReadOnlyList<SettingFieldView> FieldsWithErrors => [.. Fields.Where(f => f.HasErrors)];

    /// <summary>Email delivery status and the test buttons (Email page only).</summary>
    public EmailDiagnosticsPanel? EmailDiagnostics { get; private set; }

    /// <summary>Result of the last "Test connection" / "Send test email", written by the overview page's handlers.</summary>
    [TempData]
    public string? TestResultJson { get; set; }

    public async Task<IActionResult> OnGetAsync(string slug, string? saved, CancellationToken cancellationToken)
    {
        var settings = await Sender.Send(new GetSettingsPageQuery(slug), cancellationToken);
        if (settings is null)
        {
            return NotFound();
        }

        Saved = saved == "1";
        await ShowAsync(settings, posted: null, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string slug, CancellationToken cancellationToken)
    {
        var page = SettingsCatalog.FindBySlug(slug);
        if (page is null)
        {
            return NotFound();
        }

        var posted = PostedSettings.Read(Request.Form, page);
        try
        {
            await Sender.Send(posted.ToCommand(page.Slug), cancellationToken);
            return RedirectToPage("/Admin/Settings/Edit", new { slug = page.Slug, saved = 1 });
        }
        catch (ValidationException ex)
        {
            AddSettingErrors(page, ex);
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }

        var settings = await Sender.Send(new GetSettingsPageQuery(page.Slug), cancellationToken);
        if (settings is null)
        {
            return NotFound();
        }

        await ShowAsync(settings, posted, cancellationToken);
        return Page();
    }

    private async Task ShowAsync(SettingsPageDto settings, PostedSettings? posted, CancellationToken cancellationToken)
    {
        Settings = settings;
        Revision = posted?.Revision ?? settings.Revision;
        Fields = [.. settings.Fields.Select(f =>
        {
            var errors = ErrorsFor(f.Field.Key);
            return posted is null ? SettingFieldView.FromStored(f, errors) : SettingFieldView.FromPosted(f, posted, errors);
        })];
        ViewData["Title"] = settings.Page.Title;

        if (string.Equals(settings.Page.Slug, EmailSlug, StringComparison.OrdinalIgnoreCase))
        {
            var overview = await Sender.Send(new GetSettingsOverviewQuery(), cancellationToken);
            var result = string.IsNullOrEmpty(TestResultJson) ? null : JsonSerializer.Deserialize<EmailDiagnosticResult>(TestResultJson);
            EmailDiagnostics = new EmailDiagnosticsPanel(overview, result, currentUser.Email, settings.Page.Slug);
        }
    }

    private IReadOnlyList<string> ErrorsFor(string key) =>
        ModelState.TryGetValue(key, out var entry) ? [.. entry.Errors.Select(e => e.ErrorMessage)] : [];

    /// <summary>Errors are keyed by setting key ("" for general errors); list items may come back as "Key:2".</summary>
    private void AddSettingErrors(SettingsPage page, ValidationException ex)
    {
        foreach (var (key, messages) in ex.Errors)
        {
            var field = page.Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase))
                ?? page.Fields.FirstOrDefault(f => key.StartsWith(f.Key + ":", StringComparison.OrdinalIgnoreCase)
                                                   && int.TryParse(key[(f.Key.Length + 1)..], out _));
            foreach (var message in messages)
            {
                ModelState.AddModelError(field?.Key ?? string.Empty, message);
            }
        }

        if (ModelState.ErrorCount == 0)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }
    }
}
