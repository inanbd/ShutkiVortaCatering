using System.Collections;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Common.Settings;
using static ShutkiVorta.UnitTests.Application.Settings.SettingsTestHelpers;

namespace ShutkiVorta.UnitTests.Application.Settings;

/// <summary>The admin UI catalog must describe every stored setting exactly once, with an input that fits its type.</summary>
public sealed class SettingsCatalogTests
{
    private static readonly StringComparer Keys = StringComparer.OrdinalIgnoreCase;

    public static TheoryData<string> SectionNames => [.. ManagedSettings.Sections.Select(s => s.Name)];

    private static Dictionary<string, Type> PropertyTypes(ManagedSection section) =>
        SettingsFlattener.DescribeProperties(section.OptionsType, section.Name).ToDictionary(p => p.Path, p => p.Type, Keys);

    private static Dictionary<string, Type> AllPropertyTypes() =>
        ManagedSettings.Sections.SelectMany(s => SettingsFlattener.DescribeProperties(s.OptionsType, s.Name)).ToDictionary(p => p.Path, p => p.Type, Keys);

    [Theory]
    [MemberData(nameof(SectionNames))]
    public void EverySettableProperty_HasExactlyOneCatalogField(string sectionName)
    {
        var section = ManagedSettings.Find(sectionName)!;
        var properties = SettingsFlattener.DescribeProperties(section.OptionsType, section.Name);
        Assert.NotEmpty(properties);

        var fieldKeys = AllFields.Select(f => f.Key).ToList();
        foreach (var (path, _) in properties)
        {
            var matches = fieldKeys.Count(k => Keys.Equals(k, path));
            Assert.True(matches == 1, $"{path} has {matches} catalog fields (expected exactly 1).");
        }
    }

    [Fact]
    public void EveryCatalogField_MapsToARealProperty()
    {
        var properties = AllPropertyTypes();
        foreach (var field in AllFields)
        {
            Assert.True(properties.ContainsKey(field.Key), $"Catalog field {field.Key} has no settable options property.");
        }
    }

    [Fact]
    public void CatalogFieldKeys_AreUnique_AndUseTheExactPropertyCasing()
    {
        var keys = AllFields.Select(f => f.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(Keys).Count());

        // The key is the HTML form field name and the ModelState key, so it must match the configuration path exactly.
        var properties = AllPropertyTypes();
        foreach (var key in keys)
        {
            Assert.Equal(properties.Keys.Single(p => Keys.Equals(p, key)), key);
        }
    }

    [Fact]
    public void Slugs_AreUnique_UrlSafe_AndFoundCaseInsensitively()
    {
        var slugs = SettingsCatalog.Pages.Select(p => p.Slug).ToList();
        Assert.Equal(slugs.Count, slugs.Distinct(Keys).Count());
        Assert.All(slugs, s => Assert.Matches("^[a-z][a-z0-9-]*$", s));
        Assert.Equal(
            ["business", "ordering", "restaurants", "email", "website", "accounts", "spam-protection"],
            slugs);

        foreach (var page in SettingsCatalog.Pages)
        {
            Assert.Same(page, SettingsCatalog.FindBySlug(page.Slug.ToUpperInvariant()));
            Assert.Same(page, SettingsCatalog.FindBySection(page.Section));
        }

        Assert.Null(SettingsCatalog.FindBySlug("nope"));
        Assert.Null(SettingsCatalog.FindBySlug(null));
    }

    [Fact]
    public void EveryPage_IsAManagedSection_AndEverySectionHasOnePage()
    {
        foreach (var page in SettingsCatalog.Pages)
        {
            Assert.NotNull(ManagedSettings.Find(page.Section));
            Assert.NotEmpty(page.Fields);
            Assert.False(string.IsNullOrWhiteSpace(page.Title));
            Assert.False(string.IsNullOrWhiteSpace(page.Description));
            Assert.All(page.Fields, f => Assert.StartsWith(page.Section + ":", f.Key, StringComparison.Ordinal));
        }

        foreach (var section in ManagedSettings.Sections)
        {
            Assert.Single(SettingsCatalog.Pages, p => Keys.Equals(p.Section, section.Name));
        }
    }

    [Fact]
    public void SecretKeys_AreSecretFields_AndSecretFieldsAreSecretKeys()
    {
        Assert.NotEmpty(ManagedSettings.SecretKeys);
        foreach (var key in ManagedSettings.SecretKeys)
        {
            var field = Assert.Single(AllFields, f => Keys.Equals(f.Key, key));
            Assert.Equal(SettingKind.Secret, field.Kind);
            Assert.True(ManagedSettings.IsManagedKey(key));
        }

        foreach (var field in AllFields.Where(f => f.Kind == SettingKind.Secret))
        {
            Assert.True(ManagedSettings.IsSecret(field.Key), $"{field.Key} is a Secret field but not a secret key.");
        }

        Assert.True(ManagedSettings.IsSecret("email:smtp:password"));
        Assert.False(ManagedSettings.IsSecret("Email:Smtp:UserName"));
    }

    [Fact]
    public void ManagedKeys_AreRecognisedBySectionPrefix()
    {
        Assert.True(ManagedSettings.IsManagedKey("Ordering:ClosedDays:0"));
        Assert.True(ManagedSettings.IsManagedKey("identity:RequireConfirmedEmail"));
        Assert.False(ManagedSettings.IsManagedKey("ConnectionStrings:Sqlite"));
        Assert.False(ManagedSettings.IsManagedKey("Seed:AdminPassword"));
        Assert.False(ManagedSettings.IsManagedKey("OrderingX:Foo"));
        Assert.Null(ManagedSettings.Find("Database"));
        Assert.Equal(typeof(AccountOptions), ManagedSettings.Find("identity")!.OptionsType);
    }

    [Fact]
    public void FieldKinds_FitThePropertyTypes()
    {
        var properties = AllPropertyTypes();
        foreach (var field in AllFields)
        {
            var type = properties[field.Key];
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            var ok = field.Kind switch
            {
                SettingKind.Bool => type == typeof(bool),
                SettingKind.Integer => type == typeof(int),
                SettingKind.Decimal => underlying == typeof(decimal) || underlying == typeof(double),
                SettingKind.Money or SettingKind.Percent => underlying == typeof(decimal),
                SettingKind.DaysOfWeek => SettingsFlattener.IsListType(type) && SettingsFlattener.ElementType(type) == typeof(DayOfWeek),
                SettingKind.List => SettingsFlattener.IsListType(type) && SettingsFlattener.ElementType(type) == typeof(string),
                SettingKind.Text or SettingKind.Multiline or SettingKind.Email or SettingKind.Url or SettingKind.Phone
                    or SettingKind.Time or SettingKind.Select or SettingKind.Secret or SettingKind.TimeZone => type == typeof(string),
                _ => false,
            };
            Assert.True(ok, $"{field.Key} is a {field.Kind} field but its property is {type.Name}.");
        }
    }

    [Fact]
    public void Fields_HaveLabelsGroupsAndSensibleRanges()
    {
        foreach (var field in AllFields)
        {
            Assert.False(string.IsNullOrWhiteSpace(field.Label), field.Key);
            Assert.False(string.IsNullOrWhiteSpace(field.Group), field.Key);
            Assert.Equal(field.Key[(field.Key.LastIndexOf(':') + 1)..], field.Name);
            if (field.Min is { } min && field.Max is { } max)
            {
                Assert.True(min <= max, field.Key);
            }

            if (field.Kind is SettingKind.Select or SettingKind.DaysOfWeek)
            {
                Assert.NotEmpty(field.Choices);
                Assert.Equal(field.Choices.Count, field.Choices.Select(c => c.Value).Distinct(Keys).Count());
            }
            else
            {
                Assert.Empty(field.Choices);
            }

            if (field.Kind is SettingKind.Integer or SettingKind.Percent)
            {
                Assert.True(field.Min is not null && field.Max is not null, $"{field.Key} should declare a range.");
            }
        }
    }

    [Fact]
    public void DaysOfWeekChoices_AreTheSevenDayNames_StartingMonday()
    {
        var closedDays = Assert.Single(AllFields, f => f.Kind == SettingKind.DaysOfWeek);
        Assert.Equal("Ordering:ClosedDays", closedDays.Key);
        Assert.Equal(
            ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"],
            closedDays.Choices.Select(c => c.Value));
        Assert.All(closedDays.Choices, c => Assert.True(Enum.TryParse<DayOfWeek>(c.Value, out _)));
    }

    [Fact]
    public void TimeZones_StartWithUsZones_AreUniqueIanaIds_AndResolve()
    {
        var zones = SettingsCatalog.TimeZones;
        Assert.Equal("America/Chicago", zones[0].Value);
        Assert.Contains(zones, z => z.Value == "America/New_York");
        Assert.Contains(zones, z => z.Value == "Asia/Dhaka");
        Assert.Equal(zones.Count, zones.Select(z => z.Value).Distinct(Keys).Count());
        Assert.All(zones, z => Assert.Contains('/', z.Value));
        Assert.All(zones.Take(7), z => Assert.Matches(@"\(UTC[+-]\d\d:\d\d\)$", z.Label));
        Assert.All(zones.Take(20), z => Assert.NotNull(TimeZoneInfo.FindSystemTimeZoneById(z.Value)));
    }

    [Theory]
    [MemberData(nameof(SectionNames))]
    public void Defaults_PassValidation_AndDefaultChoicesAreListed(string sectionName)
    {
        var section = ManagedSettings.Find(sectionName)!;
        var defaults = section.CreateDefaults();
        Assert.IsType(section.OptionsType, defaults);

        var validator = ValidatorFor(section.OptionsType);
        Assert.NotNull(validator);
        var result = Validate(defaults);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")));

        var flat = SettingsFlattener.Flatten(defaults, section.Name);
        foreach (var field in SettingsCatalog.FindBySection(section.Name)!.Fields)
        {
            if (field.Kind == SettingKind.Select)
            {
                Assert.Contains(field.Choices, c => c.Value == flat[field.Key]);
            }
            else if (field.Kind == SettingKind.TimeZone)
            {
                Assert.Contains(SettingsCatalog.TimeZones, c => c.Value == flat[field.Key]);
            }
        }
    }

    [Fact]
    public void Defaults_ForANewInstallation_CloseOnMondayAndDeliverAroundDallas()
    {
        var ordering = (OrderingOptions)ManagedSettings.Find("Ordering")!.CreateDefaults();
        Assert.Equal([DayOfWeek.Monday], ordering.ClosedDays);
        Assert.Contains("752", ordering.DeliveryZipPrefixes);

        // Each call creates a fresh object, so the defaults cannot be changed by accident.
        ordering.ClosedDays.Add(DayOfWeek.Tuesday);
        Assert.Equal([DayOfWeek.Monday], ((OrderingOptions)ManagedSettings.Find("Ordering")!.CreateDefaults()).ClosedDays);
    }

    [Theory]
    [MemberData(nameof(SectionNames))]
    public void OptionClasses_StartWithEmptyLists_SoBindingNeverAppendsToADefault(string sectionName)
    {
        // The configuration binder appends list items to an existing list; a non-empty initialiser would duplicate entries.
        var section = ManagedSettings.Find(sectionName)!;
        var instance = Activator.CreateInstance(section.OptionsType)!;
        foreach (var (path, type) in SettingsFlattener.DescribeProperties(section.OptionsType, section.Name).Where(p => SettingsFlattener.IsListType(p.Type)))
        {
            var value = GetPath(instance, path) as IEnumerable;
            Assert.True(value is null || !value.Cast<object>().Any(), $"{path} ({type.Name}) is not empty on a new instance.");
        }
    }

    [Fact]
    public void NumericRanges_InTheCatalog_AreAcceptedByTheSectionValidators()
    {
        // A value the form allows must not then be rejected by the section validator (and vice versa for the bounds).
        foreach (var field in AllFields.Where(f => f.Kind is SettingKind.Integer or SettingKind.Decimal or SettingKind.Money or SettingKind.Percent))
        {
            var section = SectionOf(field.Key);
            foreach (var bound in new[] { field.Min, field.Max }.OfType<decimal>())
            {
                var options = section.CreateDefaults();
                if (field.Key == "Ordering:MinimumLeadTimeHours")
                {
                    // The notice must be shorter than the booking window, so allow booking as far ahead as the form permits.
                    SetPath(options, "Ordering:MaxDaysInAdvance", AllFields.Single(f => f.Key == "Ordering:MaxDaysInAdvance").Max);
                }

                SetPath(options, field.Key, field.Kind == SettingKind.Percent ? bound / 100m : bound);
                var errors = Validate(options).Errors.Where(e => e.PropertyName == PropertyName(field.Key)).Select(e => e.ErrorMessage).ToList();
                Assert.True(errors.Count == 0, $"{field.Key} = {bound} is allowed by the form but rejected: {string.Join(" ", errors)}");
            }
        }
    }

    private static object? GetPath(object options, string key)
    {
        object? target = options;
        foreach (var part in key.Split(':').Skip(1))
        {
            target = target?.GetType().GetProperty(part)!.GetValue(target);
        }

        return target;
    }
}
