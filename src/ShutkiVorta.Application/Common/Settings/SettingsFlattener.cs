using System.Collections;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace ShutkiVorta.Application.Common.Settings;

/// <summary>
/// Converts options objects to flat configuration keys ("Ordering:ClosedDays:0" = "Monday") and back, using the same rules as
/// the .NET configuration binder, so values stored in the database bind exactly like appsettings.json did.
/// </summary>
public static class SettingsFlattener
{
    /// <summary>Every stored key/value of an options object. Nulls are stored as "" so they override class defaults.</summary>
    public static Dictionary<string, string?> Flatten(object options, string sectionName)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        FlattenInto(result, options, sectionName);
        return result;
    }

    /// <summary>The settable property paths of an options type, e.g. "Email:Smtp:Port" (lists appear once, without an index).</summary>
    public static IReadOnlyList<(string Path, Type Type)> DescribeProperties(Type optionsType, string sectionName)
    {
        var result = new List<(string, Type)>();
        Describe(result, optionsType, sectionName);
        return result;
    }

    /// <summary>Binds flat key/values onto a fresh options object. Throws <see cref="SettingsBindingException"/> naming the key.</summary>
    public static object Bind(Type optionsType, string sectionName, IReadOnlyDictionary<string, string?> values)
    {
        var options = Activator.CreateInstance(optionsType)
            ?? throw new InvalidOperationException($"Cannot create {optionsType.Name}.");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        try
        {
            configuration.GetSection(sectionName).Bind(options);
        }
        catch (InvalidOperationException ex)
        {
            throw new SettingsBindingException(ExtractKey(ex.Message), ex);
        }

        return options;
    }

    public static T Bind<T>(string sectionName, IReadOnlyDictionary<string, string?> values) where T : class, new() =>
        (T)Bind(typeof(T), sectionName, values);

    public static bool IsListType(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    public static Type ElementType(Type listType) =>
        listType.IsArray ? listType.GetElementType()! : listType.GetGenericArguments().FirstOrDefault() ?? typeof(string);

    public static string? Format(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        bool b => b ? "true" : "false",
        Enum e => e.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static IEnumerable<PropertyInfo> SettableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetSetMethod() is not null && p.GetIndexParameters().Length == 0)
            .Where(p => p.GetCustomAttribute<SettingIgnoreAttribute>() is null);

    private static bool IsLeaf(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime)
            || t == typeof(DateOnly) || t == typeof(TimeOnly) || t == typeof(TimeSpan) || t == typeof(Guid);
    }

    private static void FlattenInto(Dictionary<string, string?> result, object options, string prefix)
    {
        foreach (var property in SettableProperties(options.GetType()))
        {
            var key = $"{prefix}:{property.Name}";
            var value = property.GetValue(options);
            if (IsLeaf(property.PropertyType))
            {
                result[key] = Format(value);
            }
            else if (IsListType(property.PropertyType))
            {
                var index = 0;
                foreach (var item in (IEnumerable?)value ?? Array.Empty<object>())
                {
                    result[$"{key}:{index++}"] = Format(item);
                }
            }
            else if (value is not null)
            {
                FlattenInto(result, value, key);
            }
        }
    }

    private static void Describe(List<(string, Type)> result, Type type, string prefix)
    {
        foreach (var property in SettableProperties(type))
        {
            var key = $"{prefix}:{property.Name}";
            if (IsLeaf(property.PropertyType) || IsListType(property.PropertyType))
            {
                result.Add((key, property.PropertyType));
            }
            else
            {
                Describe(result, property.PropertyType, key);
            }
        }
    }

    /// <summary>"Failed to convert configuration value 'abc' at 'Ordering:MaxDaysInAdvance' to type ..." → the key.</summary>
    private static string? ExtractKey(string message)
    {
        const string marker = "' at '";
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = message.IndexOf('\'', start);
        return end > start ? message[start..end] : null;
    }
}

/// <summary>A stored or submitted value could not be converted to the property's type.</summary>
public sealed class SettingsBindingException(string? key, Exception inner)
    : Exception($"The value for {key ?? "a setting"} is not valid.", inner)
{
    public string? Key { get; } = key;
}
