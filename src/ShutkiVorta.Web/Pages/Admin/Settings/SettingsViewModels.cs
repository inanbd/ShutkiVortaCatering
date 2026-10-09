using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Common.Settings;
using ShutkiVorta.Application.Features.Settings;

namespace ShutkiVorta.Web.Pages.Admin.Settings;

/// <summary>Names of the non-setting fields in the settings editor form.</summary>
public static class SettingsForm
{
    /// <summary>Hidden field holding the revision the form was loaded with (detects concurrent edits).</summary>
    public const string RevisionField = "__revision";

    /// <summary>Prefix of the checkbox that removes a stored secret, e.g. "clear:Email:Smtp:Password".</summary>
    public const string ClearPrefix = "clear:";

    /// <summary>"Email:Smtp:Port" → "setting-Email-Smtp-Port" (an id that needs no escaping in CSS selectors).</summary>
    public static string IdFor(string key) => "setting-" + key.Replace(':', '-');

    /// <summary>"Last changed Fri, Oct 9, 2026 at 7:36 AM by admin@example.com", or how the values got there.</summary>
    public static string LastChanged(DateTime? at, string? by)
    {
        if (at is not { } when)
        {
            return "Not saved yet: the built-in defaults are in use.";
        }

        if (by is not null && by.Contains("from server configuration", StringComparison.OrdinalIgnoreCase))
        {
            return $"{by} on {Format.DateTime(when)}.";
        }

        return string.IsNullOrWhiteSpace(by) ? $"Last changed {Format.DateTime(when)}." : $"Last changed {Format.DateTime(when)} by {by}.";
    }
}

/// <summary>What the admin submitted for one settings page, read straight from the form (setting keys contain colons).</summary>
public sealed class PostedSettings
{
    public int Revision { get; private init; }
    public Dictionary<string, string?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, IReadOnlyList<string>> Lists { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>List textareas exactly as typed, to show them again when the form has errors.</summary>
    public Dictionary<string, string> ListText { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string?> NewSecrets { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ClearSecrets { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static PostedSettings Read(IFormCollection form, SettingsPage page)
    {
        var posted = new PostedSettings
        {
            Revision = int.TryParse(form[SettingsForm.RevisionField], NumberStyles.Integer, CultureInfo.InvariantCulture, out var revision) ? revision : 0,
        };

        foreach (var field in page.Fields)
        {
            var raw = form[field.Key];
            switch (field.Kind)
            {
                case SettingKind.Secret:
                    var secret = raw.FirstOrDefault(v => !string.IsNullOrEmpty(v));
                    if (!string.IsNullOrEmpty(secret))
                    {
                        posted.NewSecrets[field.Key] = secret;
                    }

                    if (form[SettingsForm.ClearPrefix + field.Key].Any(IsTrue))
                    {
                        posted.ClearSecrets.Add(field.Key);
                    }

                    break;
                case SettingKind.List:
                    var text = string.Join("\n", raw.Where(v => v is not null));
                    posted.ListText[field.Key] = text;
                    posted.Lists[field.Key] = [.. text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)];
                    break;
                case SettingKind.DaysOfWeek:
                    posted.Lists[field.Key] = [.. raw.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim())];
                    break;
                case SettingKind.Bool:
                    posted.Values[field.Key] = raw.Any(IsTrue) ? "true" : "false";
                    break;
                default:
                    posted.Values[field.Key] = Single(raw);
                    break;
            }
        }

        return posted;
    }

    public UpdateSettingsPageCommand ToCommand(string slug) => new()
    {
        Slug = slug,
        Revision = Revision,
        Values = Values,
        Lists = Lists,
        NewSecrets = NewSecrets,
        ClearSecrets = ClearSecrets,
    };

    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);

    private static string? Single(StringValues values) => values.Count == 0 ? null : values[0];
}

/// <summary>One field of the settings editor as rendered: current (or just-posted) value and its errors.</summary>
public sealed record SettingFieldView(
    SettingFieldValueDto Setting,
    string? Value,
    IReadOnlyList<string> Values,
    string ListText,
    bool ClearChecked,
    bool SecretTyped,
    IReadOnlyList<string> Errors)
{
    public SettingField Field => Setting.Field;

    public string Id => SettingsForm.IdFor(Field.Key);

    public string HelpId => Id + "-help";

    public string ErrorId => Id + "-error";

    public bool HasErrors => Errors.Count > 0;

    public bool IsChecked => string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Fields that read better across the whole width of the form.</summary>
    public bool IsWide => Field.Kind is SettingKind.Multiline or SettingKind.List or SettingKind.DaysOfWeek or SettingKind.Bool;

    public string? Help => Field.Help ?? (Field.Kind == SettingKind.List ? "One per line." : null);

    public string? DescribedBy
    {
        get
        {
            var ids = new List<string>(2);
            if (!string.IsNullOrEmpty(Help))
            {
                ids.Add(HelpId);
            }

            if (HasErrors || SecretTyped)
            {
                ids.Add(ErrorId);
            }

            return ids.Count == 0 ? null : string.Join(' ', ids);
        }
    }

    public static SettingFieldView FromStored(SettingFieldValueDto setting, IReadOnlyList<string> errors) =>
        new(setting, DisplayValue(setting.Field, setting.Value), setting.Values, string.Join("\n", setting.Values), false, false, errors);

    public static SettingFieldView FromPosted(SettingFieldValueDto setting, PostedSettings posted, IReadOnlyList<string> errors)
    {
        var key = setting.Field.Key;
        return setting.Field.Kind switch
        {
            SettingKind.Secret => new(setting, null, [], string.Empty, posted.ClearSecrets.Contains(key), posted.NewSecrets.ContainsKey(key), errors),
            SettingKind.List => new(setting, null, posted.Lists.GetValueOrDefault(key) ?? [], posted.ListText.GetValueOrDefault(key) ?? string.Empty, false, false, errors),
            SettingKind.DaysOfWeek => new(setting, null, posted.Lists.GetValueOrDefault(key) ?? [], string.Empty, false, false, errors),
            _ => new(setting, posted.Values.GetValueOrDefault(key), [], string.Empty, false, false, errors),
        };
    }

    /// <summary>Stored values in the shape the inputs expect ("9:30" → "09:30" for time inputs, "5" → "5.00" for money).</summary>
    private static string? DisplayValue(SettingField field, string? value)
    {
        switch (field.Kind)
        {
            case SettingKind.Time when TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time):
                return time.ToString("HH:mm", CultureInfo.InvariantCulture);
            case SettingKind.Money when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount):
                return amount.ToString("0.00##", CultureInfo.InvariantCulture);
            default:
                return value;
        }
    }
}

/// <summary>Result of "Test connection" / "Send test email", carried across the redirect in TempData.</summary>
public sealed record EmailDiagnosticResult(string Action, bool Succeeded, bool Warning, string Message, string? Hint, IReadOnlyList<SmtpConnectionTestStep> Steps);

/// <summary>The email delivery panel shown on the settings overview and on the Email settings page.</summary>
/// <param name="ReturnTo">Settings page slug to come back to after a test, or null for the overview.</param>
public sealed record EmailDiagnosticsPanel(SettingsOverviewDto Settings, EmailDiagnosticResult? TestResult, string? TestEmailTo, string? ReturnTo);
