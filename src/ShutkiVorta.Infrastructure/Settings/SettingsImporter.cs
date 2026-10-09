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
/// Recovery switch: Settings:ReimportFromConfiguration=true copies configured values over the database once (again whenever
/// the configured values change), e.g. to repair a broken setting without the admin UI.
/// </summary>
internal sealed class SettingsImporter(SettingsRepository repository, SettingsStartupReport report, ILogger<SettingsImporter> logger)
{
    public const string ReimportSwitch = "Settings:ReimportFromConfiguration";

    /// <param name="configuration">The application configuration (files, environment variables, user secrets) — never the database.</param>
    public async Task ImportAsync(IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var reimport = configuration.GetValue(ReimportSwitch, false);
        var hasBusinessData = await repository.HasBusinessDataAsync(cancellationToken);
        var ignored = new List<string>();

        foreach (var section in ManagedSettings.Sections)
        {
            var configured = Canonicalize(section, configuration.GetSection(section.Name));
            var hash = Hash(configured);
            var (exists, importedHash) = await repository.GetImportStateAsync(section.Name, cancellationToken);

            if (exists && !(reimport && configured.Count > 0 && hash != importedHash))
            {
                if (configured.Count > 0)
                {
                    ignored.AddRange(await DifferencesAsync(section, configured, cancellationToken));
                }

                if (reimport && configured.Count > 0)
                {
                    logger.LogWarning(
                        "{Switch} is on, but these {Section} values were already re-imported, so changes made since in Admin → Settings are kept. Turn the switch off.",
                        ReimportSwitch, section.Name);
                }

                continue;
            }

            IReadOnlyDictionary<string, string?> baseline = exists
                ? (await repository.GetSectionAsync(section.Name, cancellationToken)).Values
                : SettingsFlattener.Flatten(section.CreateDefaults(), section.Name);

            var merged = Merge(section, baseline, configured);
            var options = BindLeniently(section, merged);
            var values = SettingsFlattener.Flatten(options, section.Name)
                .Where(kv => !ManagedSettings.IsSecret(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            var secrets = configured
                .Where(kv => ManagedSettings.IsSecret(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                .ToDictionary(kv => kv.Key, kv => new SecretChange(Clear: false, kv.Value), StringComparer.OrdinalIgnoreCase);

            // A live site whose settings were only in appsettings.json may have lost them in the upgrade (the file was replaced):
            // flag the section so admins are asked to check it.
            var needsReview = !exists && configured.Count == 0 && hasBusinessData;
            // The fingerprint is only recorded for a re-import, so that the switch, if left on, applies a set of values once;
            // the first import records none, so the switch can always restore the values the site was set up with.
            await repository.ImportSectionAsync(
                section.Name,
                values,
                secrets,
                exists ? "Re-imported from server configuration" : "Imported from server configuration",
                needsReview,
                exists ? hash : null,
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

        report.IgnoredConfigurationKeys = ignored;
        if (ignored.Count > 0)
        {
            logger.LogWarning(
                "These settings in appsettings.json/environment variables differ from Admin → Settings and are ignored: {Keys}. Set {Switch}=true once to copy them in.",
                string.Join(", ", ignored), ReimportSwitch);
        }
    }

    /// <summary>Configured keys of a section, with the property names' canonical casing (e.g. EMAIL__SMTP__PASSWORD → Email:Smtp:Password).</summary>
    private static Dictionary<string, string?> Canonicalize(ManagedSection section, IConfigurationSection configured)
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
                result[canonical] = value;
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
    private static Dictionary<string, string?> Merge(ManagedSection section, IReadOnlyDictionary<string, string?> baseline, IReadOnlyDictionary<string, string?> configured)
    {
        var merged = new Dictionary<string, string?>(baseline, StringComparer.OrdinalIgnoreCase);
        var lists = SettingsFlattener.DescribeProperties(section.OptionsType, section.Name)
            .Where(p => SettingsFlattener.IsListType(p.Type))
            .Select(p => p.Path);

        foreach (var list in lists)
        {
            var items = configured.Where(kv => kv.Key.StartsWith(list + ":", StringComparison.OrdinalIgnoreCase)).ToList();
            var explicitlyEmpty = configured.TryGetValue(list, out var empty) && string.IsNullOrEmpty(empty);
            if (items.Count == 0 && !explicitlyEmpty)
            {
                continue;
            }

            foreach (var key in merged.Keys.Where(k => k.StartsWith(list + ":", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                merged.Remove(key);
            }
        }

        foreach (var (key, value) in configured.Where(kv => !(SettingsFlattener.IsListType(TypeOf(section, kv.Key)) && string.IsNullOrEmpty(kv.Value))))
        {
            merged[key] = value;
        }

        return merged;
    }

    private static Type TypeOf(ManagedSection section, string key) =>
        SettingsFlattener.DescribeProperties(section.OptionsType, section.Name)
            .FirstOrDefault(p => p.Path.Equals(key, StringComparison.OrdinalIgnoreCase)).Type ?? typeof(string);

    /// <summary>Configured settings whose value differs from the database (secrets compared by presence only).</summary>
    private async Task<IEnumerable<string>> DifferencesAsync(ManagedSection section, Dictionary<string, string?> configured, CancellationToken cancellationToken)
    {
        var stored = await repository.GetSectionAsync(section.Name, cancellationToken);
        var normalized = SettingsFlattener.Flatten(BindLeniently(section, Merge(section, stored.Values, configured)), section.Name);

        return configured.Keys
            .Select(k => ListPropertyOf(section, k) ?? k)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(key => ManagedSettings.IsSecret(key)
                ? stored.Secrets.GetValueOrDefault(key, SecretState.Empty) == SecretState.Empty
                : !Same(stored.Values, normalized, key))
            .ToList();
    }

    private static string? ListPropertyOf(ManagedSection section, string key)
    {
        var colon = key.LastIndexOf(':');
        return colon > 0 && int.TryParse(key[(colon + 1)..], out _) ? key[..colon] : null;
    }

    private static bool Same(IReadOnlyDictionary<string, string?> stored, IReadOnlyDictionary<string, string?> candidate, string key)
    {
        static string Values(IReadOnlyDictionary<string, string?> source, string key) =>
            string.Join("\n", source.Where(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase) || kv.Key.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => $"{kv.Key.ToUpperInvariant()}={kv.Value}"));

        return Values(stored, key) == Values(candidate, key);
    }

    /// <summary>Binds the merged values; a value that cannot be converted is dropped (with a warning) instead of failing startup.</summary>
    private object BindLeniently(ManagedSection section, Dictionary<string, string?> values)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                return SettingsFlattener.Bind(section.OptionsType, section.Name, values);
            }
            catch (SettingsBindingException ex) when (ex.Key is not null && values.Remove(ex.Key))
            {
                logger.LogWarning("Ignoring the setting {Key}: its value could not be converted", ex.Key);
            }
        }

        return section.CreateDefaults();
    }

    /// <summary>Fingerprint of the configured values. Secret values are left out (only their presence counts), so it reveals nothing.</summary>
    private static string Hash(Dictionary<string, string?> configured)
    {
        var text = string.Join("\n", configured
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key.ToUpperInvariant()}={(ManagedSettings.IsSecret(kv.Key) ? "(set)" : kv.Value)}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}

/// <summary>What the importer found at startup, shown to admins (implements <see cref="ISettingsDiagnostics"/> with the provider).</summary>
internal sealed class SettingsStartupReport(SettingsConfiguration settings) : ISettingsDiagnostics
{
    public IReadOnlyList<string> IgnoredConfigurationKeys { get; set; } = [];

    public IReadOnlyList<string> RejectedKeys => settings.Provider.RejectedKeys;
}
