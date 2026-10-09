namespace ShutkiVorta.Application.Common.Settings;

/// <summary>Marks a settable options property that is computed at runtime and never stored as a setting.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingIgnoreAttribute : Attribute;
