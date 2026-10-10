using ShutkiVorta.Domain.Common;

namespace ShutkiVorta.Domain.Inventory;

/// <summary>
/// An ingredient or supply the kitchen buys (dried fish, mustard oil, deli containers...). Names are saved the first time
/// they are used and suggested on later purchases; a name matches regardless of case and extra spaces.
/// </summary>
public sealed class InventoryItem : Entity
{
    public const int MaxNameLength = 100;
    public const int MaxUnitLength = 20;
    public const string DefaultUnit = "lb";

    // Required by the data mapper.
    private InventoryItem()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Upper-case name with single spaces: the key that makes "mustard  oil" and "Mustard Oil" the same item.</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    /// <summary>The unit suggested when the item is picked again (the one used most recently).</summary>
    public string Unit { get; private set; } = DefaultUnit;

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static InventoryItem Create(string name, string? unit, DateTime nowUtc)
    {
        var item = new InventoryItem { CreatedAtUtc = nowUtc };
        item.Update(name, string.IsNullOrWhiteSpace(unit) ? DefaultUnit : unit, nowUtc);
        return item;
    }

    public void Update(string name, string unit, DateTime nowUtc)
    {
        Name = CleanName(name);
        NormalizedName = Name.ToUpperInvariant();
        Unit = CleanUnit(unit);
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Trims the name and collapses runs of whitespace. Throws when it is empty or too long.</summary>
    public static string CleanName(string? name) =>
        Guard.NotEmpty(CollapseWhitespace(name), "Item name", MaxNameLength);

    public static string Normalize(string? name) => CleanName(name).ToUpperInvariant();

    /// <summary>Units are stored in lower case ("lb", "bottle") so totals of the same unit add up.</summary>
    public static string CleanUnit(string? unit) =>
        Guard.NotEmpty(CollapseWhitespace(unit), "Unit", MaxUnitLength).ToLowerInvariant();

    private static string CollapseWhitespace(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
