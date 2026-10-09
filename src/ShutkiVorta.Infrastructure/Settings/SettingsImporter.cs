using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShutkiVorta.Application.Common.Settings;
using ShutkiVorta.Application.Features.Settings;

namespace ShutkiVorta.Infrastructure.Settings;

/// <summary>
/// Fills the database with settings on first start: values still present in appsettings.json, appsettings.{Environment}.json,
/// user secrets or environment variables are imported; everything else gets the built-in defaults. After that the database is
/// the source of truth (Admin → Settings) and those values are ignored (and reported to admins if they differ).
/// Recovery switch: Settings:ReimportFromConfiguration=true copies the configured values over the database once each time it
/// is turned on (and again if the configured values, other than passwords, change while it stays on), e.g. to repair a
/// broken setting without the admin UI.
/// </summary>
internal sealed class SettingsImporter(SettingsRepository repository, SettingsStartupReport report, ILogger<SettingsImporter> logger)
{
    public const string ReimportSwitch = "Settings:ReimportFromConfiguration";

    /// <param name="configuration">The application configuration (files, environment variables, user secrets) — never the database.</param>
    public async Task ImportAsync(IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var reimport = configuration.GetValue(ReimportSwitch, false);
        if (!reimport)
        {
            // Switched off: the next time it is turned on, it applies the configured values again.
            await repository.ClearImportHashesAsync(cancellationToken);
        }

        var hasBusinessData = await repository.HasBusinessDataAsync(cancellationToken);
        var configuredBySection = new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in ManagedSettings.Sections)
        {
            var configured = ConfiguredSettings.Read(section, configuration.GetSection(section.Name));
            configuredBySection[section.Name] = configured;
            var hash = ConfiguredSettings.Hash(configured);
            var (exists, appliedHash) = await repository.GetImportStateAsync(section.Name, cancellationToken);

            if (exists && !(reimport && configured.Count > 0 && hash != appliedHash))
            {
                if (reimport && configured.Count > 0)
                {
                    logger.LogWarning(
                        "{Switch} is on, but the configured {Section} values were already copied in, so changes made since in Admin → Settings are kept. Turn the switch off.",
                        ReimportSwitch, section.Name);
                }

                continue;
            }

            IReadOnlyDictionary<string, string?> baseline = exists
                ? (await repository.GetSectionAsync(section.Name, cancellationToken)).Values
                : SettingsFlattener.Flatten(section.CreateDefaults(), section.Name);

            var options = ConfiguredSettings.BindLeniently(
                section, ConfiguredSettings.Merge(section, baseline, configured), key => logger.LogWarning("Ignoring the setting {Key}: its value could not be converted", key));
            var values = SettingsFlattener.Flatten(options, section.Name)
                .Where(kv => !ManagedSettings.IsSecret(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            var secrets = configured
                .Where(kv => ManagedSettings.IsSecret(kv.Key))
                .ToDictionary(kv => kv.Key, kv => new SecretChange(Clear: false, kv.Value), StringComparer.OrdinalIgnoreCase);

            // A live site whose settings were only in appsettings.json may have lost them in the upgrade (the file was replaced,
            // perhaps leaving only a password in an environment variable or an empty entry): flag the section for admins to check.
            var needsReview = !exists && hasBusinessData
                && !configured.Any(kv => !ManagedSettings.IsSecret(kv.Key) && !string.IsNullOrEmpty(kv.Value));

            // The fingerprint is only kept while the switch is on, so that a start with the switch still on does not apply the
            // same values again over later changes, while turning it off and on again always applies them.
            await repository.ImportSectionAsync(
                section.Name,
                values,
                secrets,
                exists ? "Re-imported from server configuration" : "Imported from server configuration",
                needsReview,
                reimport ? hash : null,
                cancellationToken);

            if (exists)
            {
                logger.LogWarning("Re-imported {Count} {Section} setting(s) from the server configuration ({Switch}); turn the switch off again", configured.Count, section.Name, ReimportSwitch);
            }
            else if (needsReview)
            {
                logger.LogError(
                    "The {Section} settings were not found in the server configuration, so defaults were stored. Check them in Admin → Settings.", section.Name);
            }
            else
            {
                logger.LogInformation("Imported the {Section} settings into the database ({Count} value(s) from the server configuration)", section.Name, configured.Count);
            }
        }

        report.Configured = configuredBySection;
    }

    /// <summary>Logs configured values that differ from the saved settings (call once the saved settings are loaded).</summary>
    public void ReportIgnoredConfiguration()
    {
        var ignored = report.IgnoredConfigurationKeys;
        if (ignored.Count > 0)
        {
            logger.LogWarning(
                "These settings in appsettings.json/environment variables differ from Admin → Settings and are ignored: {Keys}. Set {Switch}=true once to copy them in.",
                string.Join(", ", ignored), ReimportSwitch);
        }
    }
}

/// <summary>Reading, merging and comparing settings that are present in the application configuration.</summary>
internal static class ConfiguredSettings
{
    /// <summary>
    /// Configured keys of a section, with the property names' canonical casing (e.g. EMAIL__SMTP__PASSWORD → Email:Smtp:Password).
    /// An empty secret ("Password": "") counts as not configured.
    /// </summary>
    public static Dictionary<string, string?> Read(ManagedSection section, IConfigurationSection configured)
    {
        var properties = SettingsFlattener.DescribeProperties(section.OptionsType, section.Name)
            .ToDictionary(p => p.Path, p => p.Path, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in configured.AsEnumerable(makePathsRelative: false))
        {
            if (value is null)
            {
                continue;
            }

            if (properties.TryGetValue(key, out var canonical))
            {
                if (!(ManagedSettings.IsSecret(canonical) && value.Length == 0))
                {
                    result[canonical] = value;
                }

                continue;
            }

            var colon = key.LastIndexOf(':');
            if (colon > 0 && int.TryParse(key[(colon + 1)..], out var index) && properties.TryGetValue(key[..colon], out var list))
            {
                result[$"{list}:{index}"] = value;
            }
        }

        return result;
    }

    /// <summary>Configured keys override the baseline; a configured list (even an empty one) replaces the whole list.</summary>
    public static Dictionary<string, string?> Merge(ManagedSection section, IReadOnlyDictionary<string, string?> baseline, IReadOnlyDictionary<string, string?> configured)
    {
        var merged = new Dictionary<string, string?>(baseline, StringComparer.OrdinalIgnoreCase);
        var properties = SettingsFlattener.DescribeProperties(section.OptionsType, section.Name).ToList();
        var lists = properties.Where(p => SettingsFlattener.IsListType(p.Type)).Select(p => p.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var list in lists)
        {
            var items = configured.Keys.Any(k => k.StartsWith(list + ":", StringComparison.OrdinalIgnoreCase));
            var explicitlyEmpty = configured.TryGetValue(list, out var empty) && string.IsNullOrEmpty(empty);
            if (!items && !explicitlyEmpty)
            {
                continue;
            }

            foreach (var key in merged.Keys.Where(k => k.StartsWith(list + ":", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                merged.Remove(key);
            }
        }

        foreach (var (key, value) in configured.Where(kv => !lists.Contains(kv.Key)))
        {
            merged[key] = value;
        }

        return merged;
    }

    /// <summary>Binds the merged values; a value that cannot be converted is dropped instead of failing.</summary>
    public static object BindLeniently(ManagedSection section, Dictionary<string, string?> values, Action<string>? dropped = null)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                return SettingsFlattener.Bind(section.OptionsType, section.Name, values);
            }
            catch (SettingsBindingException ex) when (ex.Key is not null && values.Remove(ex.Key))
            {
                dropped?.Invoke(ex.Key);
            }
        }

        return section.CreateDefaults();
    }

    /// <summary>
    /// Configured settings whose value differs from the saved one (a list counts as one setting; a secret only counts when
    /// nothing is saved, since saved secrets are encrypted).
    /// </summary>
    public static IEnumerable<string> Differences(ManagedSection section, IReadOnlyDictionary<string, string?> configured, IReadOnlyDictionary<string, string?> saved)
    {
        if (configured.Count == 0)
        {
            return [];
        }

        var candidate = SettingsFlattener.Flatten(BindLeniently(section, Merge(section, saved, configured)), section.Name);
        return configured.Keys
            .Select(k => ListPropertyOf(k) ?? k)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(key => ManagedSettings.IsSecret(key)
                ? string.IsNullOrEmpty(saved.GetValueOrDefault(key))
                : !Same(saved, candidate, key))
            .ToList();
    }

    /// <summary>Fingerprint of the configured values. Secret values are left out (only their presence counts), so it reveals nothing.</summary>
    public static string Hash(IReadOnlyDictionary<string, string?> configured)
    {
        var text = string.Join("\n", configured
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key.ToUpperInvariant()}={(ManagedSettings.IsSecret(kv.Key) ? "(set)" : kv.Value)}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string? ListPropertyOf(string key)
    {
        var colon = key.LastIndexOf(':');
        return colon > 0 && int.TryParse(key[(colon + 1)..], out _) ? key[..colon] : null;
    }

    private static bool Same(IReadOnlyDictionary<string, string?> saved, IReadOnlyDictionary<string, string?> candidate, string key)
    {
        static string Values(IReadOnlyDictionary<string, string?> source, string key) =>
            string.Join("\n", source.Where(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase) || kv.Key.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => $"{kv.Key.ToUpperInvariant()}={kv.Value}"));

        return Values(saved, key) == Values(candidate, key);
    }
}

/// <summary>
/// Settings problems shown to admins: saved values that could not be used, and configured values that are ignored because they
/// differ from the saved ones (worked out from the settings currently in use, so it follows every save).
/// </summary>
internal sealed class SettingsStartupReport(SettingsConfiguration settings) : ISettingsDiagnostics
{
    /// <summary>The managed settings found in the application configuration at startup, by section.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>> Configured { get; set; } =
        new Dictionary<string, IReadOnlyDictionary<string, string?>>();

    public IReadOnlyList<string> RejectedKeys => settings.Provider.RejectedKeys;

    public IReadOnlyList<string> IgnoredConfigurationKeys
    {
        get
        {
            var saved = settings.Provider.Current;
            return [.. ManagedSettings.Sections
                .Where(section => Configured.ContainsKey(section.Name))
                .SelectMany(section => ConfiguredSettings.Differences(
                    section,
                    Configured[section.Name],
                    saved.Where(kv => kv.Key.StartsWith(section.Name + ":", StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)))];
        }
    }
}
