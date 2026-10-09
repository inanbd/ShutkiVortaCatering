using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShutkiVorta.Application.Common.Settings;
using ShutkiVorta.Application.Features.Settings;

namespace ShutkiVorta.Infrastructure.Settings;

/// <summary>
/// Fills the database with settings on first start: values still present in appsettings.json, appsettings.{Environment}.json,
/// user secrets or environment variables are imported; everything else gets the built-in defaults. After that the database is
/// the source of truth (Admin → Settings). Set Settings:ReimportFromConfiguration=true to overwrite stored values with whatever
/// the configuration files/environment contain on the next start (a recovery switch).
/// </summary>
internal sealed class SettingsImporter(SettingsRepository repository, ILogger<SettingsImporter> logger)
{
    public const string ReimportSwitch = "Settings:ReimportFromConfiguration";

    /// <param name="configuration">Configuration WITHOUT the database source (files, environment, user secrets).</param>
    public async Task ImportAsync(IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var reimport = configuration.GetValue(ReimportSwitch, false);
        if (reimport)
        {
            logger.LogWarning(
                "{Switch} is on: settings present in appsettings.json/environment variables overwrite the values saved in Admin → Settings on every start. Turn it off once the settings are fixed.",
                ReimportSwitch);
        }
        foreach (var section in ManagedSettings.Sections)
        {
            var exists = await repository.SectionExistsAsync(section.Name, cancellationToken);
            var configured = configuration.GetSection(section.Name)
                .AsEnumerable(makePathsRelative: false)
                .Where(kv => kv.Value is not null && kv.Key.Contains(':'))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            if (exists && !(reimport && configured.Count > 0))
            {
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

            await repository.ImportSectionAsync(
                section.Name, values, secrets, exists ? "Re-imported from configuration" : "Imported from configuration", cancellationToken);
            logger.LogInformation(
                "{Action} the {Section} settings into the database ({Count} value(s) from configuration files/environment)",
                exists ? "Re-imported" : "Imported", section.Name, configured.Count);
        }
    }

    /// <summary>Configured keys override the baseline; a configured list replaces the whole baseline list.</summary>
    private static Dictionary<string, string?> Merge(ManagedSection section, IReadOnlyDictionary<string, string?> baseline, IReadOnlyDictionary<string, string?> configured)
    {
        var merged = new Dictionary<string, string?>(baseline, StringComparer.OrdinalIgnoreCase);
        var lists = SettingsFlattener.DescribeProperties(section.OptionsType, section.Name)
            .Where(p => SettingsFlattener.IsListType(p.Type))
            .Select(p => p.Path + ":");

        foreach (var list in lists.Where(prefix => configured.Keys.Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))))
        {
            foreach (var key in merged.Keys.Where(k => k.StartsWith(list, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                merged.Remove(key);
            }
        }

        foreach (var (key, value) in configured)
        {
            merged[key] = value;
        }

        return merged;
    }

    /// <summary>Binds the merged values; a value that cannot be converted is dropped (with a warning) instead of failing startup.</summary>
    private object BindLeniently(ManagedSection section, Dictionary<string, string?> values)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                return SettingsFlattener.Bind(section.OptionsType, section.Name, values);
            }
            catch (SettingsBindingException ex) when (ex.Key is not null && values.Remove(ex.Key))
            {
                logger.LogWarning("Ignoring the setting {Key}: its value could not be converted ({Message})", ex.Key, ex.InnerException?.Message);
            }
        }

        return section.CreateDefaults();
    }
}
