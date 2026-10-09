using System.Globalization;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Common.Settings;
using ValidationException = ShutkiVorta.Application.Common.Exceptions.ValidationException;

namespace ShutkiVorta.Application.Features.Settings;

public enum SecretState
{
    Empty,
    Saved,

    /// <summary>A value is stored but cannot be decrypted (the data-protection keys changed); it must be entered again.</summary>
    Unreadable,
}

/// <summary>What one section looks like in the database right now.</summary>
public sealed record StoredSection(
    IReadOnlyDictionary<string, string?> Values,
    IReadOnlyDictionary<string, SecretState> Secrets,
    int Revision,
    DateTime? LastChangedAtUtc,
    string? LastChangedBy);

/// <summary>How a secret setting changes when a section is saved.</summary>
public sealed record SecretChange(bool Clear, string? NewValue);

/// <summary>Database-backed settings (see <see cref="ManagedSettings"/>). Saving makes the new values live immediately.</summary>
public interface ISettingsStore
{
    Task<StoredSection> GetSectionAsync(string section, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces every value of the section. Secrets not listed in <paramref name="secrets"/> keep their stored value.
    /// Throws <see cref="SettingsConflictException"/> when someone else saved since <paramref name="expectedRevision"/>.
    /// </summary>
    Task<int> SaveSectionAsync(
        string section,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, SecretChange> secrets,
        int expectedRevision,
        string changedBy,
        CancellationToken cancellationToken = default);
}

public sealed class SettingsConflictException() : Exception("The settings were changed by someone else while you were editing.");

/// <summary>Published after a settings section was saved (e.g. to refresh cached robots.txt/sitemap).</summary>
public sealed record SettingsChangedNotification(string Section) : INotification;

// ---------------------------------------------------------------------------------------------------------------------

public sealed record SettingsPageSummaryDto(SettingsPage Page, DateTime? LastChangedAtLocal, string? LastChangedBy);

public sealed record GetSettingsPagesQuery : IRequest<IReadOnlyList<SettingsPageSummaryDto>>, IRequireAdmin;

public sealed record SettingFieldValueDto(SettingField Field, string? Value, IReadOnlyList<string> Values, SecretState Secret)
{
    public IReadOnlyList<SettingChoice> Choices => Field.Kind == SettingKind.TimeZone ? SettingsCatalog.TimeZones : Field.Choices;
}

public sealed record SettingsPageDto(SettingsPage Page, IReadOnlyList<SettingFieldValueDto> Fields, int Revision, DateTime? LastChangedAtLocal, string? LastChangedBy);

/// <summary>A settings page with its current values, ready to edit (percentages as 8.25, secrets never included).</summary>
public sealed record GetSettingsPageQuery(string Slug) : IRequest<SettingsPageDto?>, IRequireAdmin;

/// <summary>
/// Saves one settings page. <see cref="Values"/> holds single values as typed in the form (keyed by setting key), <see cref="Lists"/>
/// list and day values, <see cref="NewSecrets"/> newly typed secrets (blank = keep) and <see cref="ClearSecrets"/> secrets to remove.
/// </summary>
public sealed record UpdateSettingsPageCommand : IRequest<int>, IRequireAdmin
{
    public required string Slug { get; init; }
    public int Revision { get; init; }
    public IReadOnlyDictionary<string, string?> Values { get; init; } = new Dictionary<string, string?>();
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Lists { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
    public IReadOnlyDictionary<string, string?> NewSecrets { get; init; } = new Dictionary<string, string?>();
    public IReadOnlyCollection<string> ClearSecrets { get; init; } = [];
}

internal sealed class ManagedSettingsHandlers(
    ISettingsStore store,
    IServiceProvider services,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IPublisher publisher) :
    IRequestHandler<GetSettingsPagesQuery, IReadOnlyList<SettingsPageSummaryDto>>,
    IRequestHandler<GetSettingsPageQuery, SettingsPageDto?>,
    IRequestHandler<UpdateSettingsPageCommand, int>
{
    public async Task<IReadOnlyList<SettingsPageSummaryDto>> Handle(GetSettingsPagesQuery request, CancellationToken cancellationToken)
    {
        var result = new List<SettingsPageSummaryDto>();
        foreach (var page in SettingsCatalog.Pages)
        {
            var stored = await store.GetSectionAsync(page.Section, cancellationToken);
            result.Add(new SettingsPageSummaryDto(page, ToLocal(stored.LastChangedAtUtc), stored.LastChangedBy));
        }

        return result;
    }

    public async Task<SettingsPageDto?> Handle(GetSettingsPageQuery request, CancellationToken cancellationToken)
    {
        var page = SettingsCatalog.FindBySlug(request.Slug);
        if (page is null)
        {
            return null;
        }

        var stored = await store.GetSectionAsync(page.Section, cancellationToken);
        var fields = page.Fields.Select(f => f.Kind switch
        {
            SettingKind.Secret => new SettingFieldValueDto(f, null, [], stored.Secrets.GetValueOrDefault(f.Key, SecretState.Empty)),
            SettingKind.List or SettingKind.DaysOfWeek => new SettingFieldValueDto(f, null, ListValues(stored.Values, f.Key), SecretState.Empty),
            _ => new SettingFieldValueDto(f, SettingValueFormat.ToDisplay(f, stored.Values.GetValueOrDefault(f.Key)), [], SecretState.Empty),
        }).ToList();

        return new SettingsPageDto(page, fields, stored.Revision, ToLocal(stored.LastChangedAtUtc), stored.LastChangedBy);
    }

    public async Task<int> Handle(UpdateSettingsPageCommand request, CancellationToken cancellationToken)
    {
        var page = SettingsCatalog.FindBySlug(request.Slug) ?? throw new ValidationException("Unknown settings page.");
        var section = ManagedSettings.Find(page.Section) ?? throw new ValidationException("Unknown settings section.");
        var propertyTypes = SettingsFlattener.DescribeProperties(section.OptionsType, section.Name)
            .ToDictionary(p => p.Path, p => p.Type, StringComparer.OrdinalIgnoreCase);

        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var secrets = new Dictionary<string, SecretChange>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in page.Fields)
        {
            switch (field.Kind)
            {
                case SettingKind.Secret:
                    if (request.ClearSecrets.Contains(field.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        secrets[field.Key] = new SecretChange(Clear: true, null);
                    }
                    else if (request.NewSecrets.TryGetValue(field.Key, out var secret) && !string.IsNullOrEmpty(secret))
                    {
                        secrets[field.Key] = new SecretChange(Clear: false, secret);
                    }

                    break;
                case SettingKind.List or SettingKind.DaysOfWeek:
                    var items = (request.Lists.GetValueOrDefault(field.Key) ?? [])
                        .Select(i => i?.Trim())
                        .Where(i => !string.IsNullOrEmpty(i))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    for (var i = 0; i < items.Count; i++)
                    {
                        values[$"{field.Key}:{i}"] = items[i];
                    }

                    break;
                default:
                    var raw = request.Values.GetValueOrDefault(field.Key);
                    var nullable = propertyTypes.TryGetValue(field.Key, out var type) && Nullable.GetUnderlyingType(type) is not null;
                    if (SettingValueFormat.TryParse(field, raw, nullable, out var canonical, out var error))
                    {
                        values[field.Key] = canonical;
                    }
                    else
                    {
                        AddError(errors, field.Key, error);
                    }

                    break;
            }
        }

        object options;
        try
        {
            options = SettingsFlattener.Bind(section.OptionsType, section.Name, values);
        }
        catch (SettingsBindingException ex)
        {
            AddError(errors, ex.Key ?? string.Empty, "This value is not valid.");
            throw ToValidationException(errors);
        }

        if (services.GetService(typeof(IValidator<>).MakeGenericType(section.OptionsType)) is IValidator validator)
        {
            var result = await validator.ValidateAsync(new ValidationContext<object>(options), cancellationToken);
            foreach (var failure in result.Errors)
            {
                AddError(errors, FieldKeyFor(section.Name, failure.PropertyName), failure.ErrorMessage);
            }
        }

        if (errors.Count > 0)
        {
            throw ToValidationException(errors);
        }

        // Store the canonical form of every property; secrets are handled separately and never re-written from the form.
        var canonicalValues = SettingsFlattener.Flatten(options, section.Name)
            .Where(kv => !ManagedSettings.IsSecret(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        int revision;
        try
        {
            revision = await store.SaveSectionAsync(section.Name, canonicalValues, secrets, request.Revision, currentUser.Email ?? "Admin", cancellationToken);
        }
        catch (SettingsConflictException ex)
        {
            throw new ValidationException(ex.Message + " Please reload the page and make your changes again.");
        }

        await publisher.Publish(new SettingsChangedNotification(section.Name), cancellationToken);
        return revision;
    }

    private DateTime? ToLocal(DateTime? utc) => utc is { } value ? clock.ToBusinessTime(value) : null;

    private static IReadOnlyList<string> ListValues(IReadOnlyDictionary<string, string?> values, string key) =>
        [.. values
            .Where(kv => kv.Key.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(kv.Key[(key.Length + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .OrderBy(kv => int.Parse(kv.Key[(key.Length + 1)..], CultureInfo.InvariantCulture))
            .Select(kv => kv.Value ?? string.Empty)];

    /// <summary>"Smtp.Port" → "Email:Smtp:Port"; "AdminRecipients[2]" → "Email:AdminRecipients".</summary>
    private static string FieldKeyFor(string section, string propertyName)
    {
        var path = propertyName;
        var bracket = path.IndexOf('[');
        if (bracket >= 0)
        {
            path = path[..bracket];
        }

        return string.IsNullOrEmpty(path) ? string.Empty : $"{section}:{path.Replace('.', ':')}";
    }

    private static void AddError(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var list))
        {
            errors[key] = list = [];
        }

        if (!list.Contains(message))
        {
            list.Add(message);
        }
    }

    private static ValidationException ToValidationException(Dictionary<string, List<string>> errors) =>
        new(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
}

/// <summary>Converts between stored (invariant) values and what admins see and type.</summary>
public static class SettingValueFormat
{
    public static string? ToDisplay(SettingField field, string? stored)
    {
        if (field.Kind == SettingKind.Percent && decimal.TryParse(stored, NumberStyles.Number, CultureInfo.InvariantCulture, out var fraction))
        {
            return (fraction * 100m).ToString("0.####", CultureInfo.InvariantCulture);
        }

        return stored;
    }

    public static bool TryParse(SettingField field, string? raw, bool nullable, out string? canonical, out string error)
    {
        var text = raw?.Trim() ?? string.Empty;
        canonical = text;
        error = string.Empty;

        switch (field.Kind)
        {
            case SettingKind.Bool:
                canonical = text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("on", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
                return true;
            case SettingKind.Integer:
                if (text.Length == 0)
                {
                    return Empty(nullable, out canonical, out error);
                }

                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                {
                    error = "Please enter a whole number.";
                    return false;
                }

                canonical = whole.ToString(CultureInfo.InvariantCulture);
                return InRange(field, whole, out error);
            case SettingKind.Decimal or SettingKind.Money or SettingKind.Percent:
                if (text.Length == 0)
                {
                    return Empty(nullable, out canonical, out error);
                }

                if (!decimal.TryParse(text.TrimStart('$').TrimEnd('%').Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    error = "Please enter a number, e.g. 12.50.";
                    return false;
                }

                if (!InRange(field, number, out error))
                {
                    return false;
                }

                canonical = (field.Kind == SettingKind.Percent ? number / 100m : number).ToString(CultureInfo.InvariantCulture);
                return true;
            case SettingKind.Time:
                if (!TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                {
                    error = "Please enter a time such as 09:30 or 17:00.";
                    return false;
                }

                canonical = time.ToString("HH:mm", CultureInfo.InvariantCulture);
                return true;
            case SettingKind.Select or SettingKind.TimeZone:
                var choices = field.Kind == SettingKind.TimeZone ? SettingsCatalog.TimeZones : field.Choices;
                var match = choices.FirstOrDefault(c => string.Equals(c.Value, text, StringComparison.OrdinalIgnoreCase));
                if (match is null && field.Kind == SettingKind.Select)
                {
                    error = "Please choose an option from the list.";
                    return false;
                }

                canonical = match?.Value ?? text; // Unlisted time zones are checked by the section validator.
                return true;
            default:
                return true;
        }
    }

    private static bool Empty(bool nullable, out string? canonical, out string error)
    {
        canonical = string.Empty;
        error = nullable ? string.Empty : "This field is required.";
        return nullable;
    }

    private static bool InRange(SettingField field, decimal value, out string error)
    {
        error = string.Empty;
        if (field.Min is { } min && value < min || field.Max is { } max && value > max)
        {
            error = $"Please enter a value between {field.Min?.ToString("0.####", CultureInfo.InvariantCulture)} and {field.Max?.ToString("0.####", CultureInfo.InvariantCulture)}.";
            return false;
        }

        return true;
    }
}
