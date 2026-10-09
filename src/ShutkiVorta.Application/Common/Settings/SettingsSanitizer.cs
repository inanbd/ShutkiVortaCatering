namespace ShutkiVorta.Application.Common.Settings;

/// <summary>
/// Keeps only stored settings that bind cleanly, so one bad value (a hand-edited row, a NULL, a value left over from an older
/// version) can never break the site: it is dropped, the built-in default applies, and the key is reported to admins.
/// </summary>
public static class SettingsSanitizer
{
    public static (Dictionary<string, string?> Clean, List<string> Rejected) Sanitize(IReadOnlyDictionary<string, string?> stored)
    {
        var clean = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var rejected = new List<string>();

        foreach (var section in ManagedSettings.Sections)
        {
            var properties = SettingsFlattener.DescribeProperties(section.OptionsType, section.Name)
                .ToDictionary(p => p.Path, p => p.Type, StringComparer.OrdinalIgnoreCase);
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var (key, value) in stored.Where(kv => kv.Key.StartsWith(section.Name + ":", StringComparison.OrdinalIgnoreCase)))
            {
                if (!TryGetType(properties, key, out var type, out var isListItem))
                {
                    continue; // Not (or no longer) a setting: ignore it.
                }

                if (value is null)
                {
                    if (isListItem || (type.IsValueType && Nullable.GetUnderlyingType(type) is null))
                    {
                        rejected.Add(key);
                        continue;
                    }

                    values[key] = string.Empty;
                    continue;
                }

                values[key] = value;
            }

            while (true)
            {
                try
                {
                    SettingsFlattener.Bind(section.OptionsType, section.Name, values);
                    break;
                }
                catch (SettingsBindingException ex) when (ex.Key is not null && values.Remove(ex.Key))
                {
                    rejected.Add(ex.Key);
                }
                catch (SettingsBindingException)
                {
                    rejected.AddRange(values.Keys);
                    values.Clear();
                    break;
                }
            }

            foreach (var (key, value) in values)
            {
                clean[key] = value;
            }
        }

        return (clean, rejected);
    }

    /// <summary>"Ordering:TaxRate" → decimal; "Ordering:ClosedDays:2" → the list's element type (isListItem).</summary>
    private static bool TryGetType(Dictionary<string, Type> properties, string key, out Type type, out bool isListItem)
    {
        isListItem = false;
        if (properties.TryGetValue(key, out type!))
        {
            return !SettingsFlattener.IsListType(type);
        }

        var colon = key.LastIndexOf(':');
        if (colon > 0 && int.TryParse(key[(colon + 1)..], out _)
            && properties.TryGetValue(key[..colon], out var listType) && SettingsFlattener.IsListType(listType))
        {
            isListItem = true;
            type = SettingsFlattener.ElementType(listType);
            return true;
        }

        return false;
    }
}
