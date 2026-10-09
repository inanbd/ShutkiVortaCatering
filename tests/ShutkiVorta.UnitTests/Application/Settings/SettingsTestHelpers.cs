using System.Globalization;
using FluentValidation;
using ShutkiVorta.Application.Common.Settings;

namespace ShutkiVorta.UnitTests.Application.Settings;

/// <summary>Reflection helpers shared by the settings tests.</summary>
internal static class SettingsTestHelpers
{
    /// <summary>The application's validators, found the same way the application registers them.</summary>
    public static IServiceProvider Validators { get; } = new ValidatorProvider();

    public static IValidator? ValidatorFor(Type optionsType) =>
        Validators.GetService(typeof(IValidator<>).MakeGenericType(optionsType)) as IValidator;

    public static FluentValidation.Results.ValidationResult Validate(object options) =>
        ValidatorFor(options.GetType())!.Validate(new ValidationContext<object>(options));

    public static IEnumerable<SettingField> AllFields => SettingsCatalog.Pages.SelectMany(p => p.Fields);

    public static ManagedSection SectionOf(string key) =>
        ManagedSettings.Sections.Single(s => key.StartsWith(s.Name + ":", StringComparison.OrdinalIgnoreCase));

    /// <summary>"Email:Smtp:Port" → "Smtp.Port" (the property name FluentValidation reports).</summary>
    public static string PropertyName(string key) => string.Join('.', key.Split(':').Skip(1));

    /// <summary>Sets the property at a configuration path ("Email:Smtp:Port") on an options object.</summary>
    public static void SetPath(object options, string key, object? value)
    {
        var parts = key.Split(':').Skip(1).ToArray();
        var target = options;
        foreach (var part in parts[..^1])
        {
            target = target.GetType().GetProperty(part)!.GetValue(target)!;
        }

        var property = target.GetType().GetProperty(parts[^1]) ?? throw new InvalidOperationException($"No property for {key}.");
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        property.SetValue(target, value is null ? null : Convert.ChangeType(value, type, CultureInfo.InvariantCulture));
    }

    /// <summary>Runs <paramref name="action"/> with <paramref name="culture"/> as the current culture.</summary>
    public static void WithCulture(string culture, Action action)
    {
        var (current, currentUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = current;
            CultureInfo.CurrentUICulture = currentUi;
        }
    }

    /// <summary>Resolves IValidator&lt;T&gt; for the validators in the Application assembly (internal ones included).</summary>
    private sealed class ValidatorProvider : IServiceProvider
    {
        private readonly Dictionary<Type, Type> _validators = AssemblyScanner
            .FindValidatorsInAssembly(typeof(ManagedSettings).Assembly, includeInternalTypes: true)
            .ToDictionary(r => r.InterfaceType, r => r.ValidatorType);

        public object? GetService(Type serviceType) =>
            _validators.TryGetValue(serviceType, out var type) ? Activator.CreateInstance(type, nonPublic: true) : null;
    }
}
