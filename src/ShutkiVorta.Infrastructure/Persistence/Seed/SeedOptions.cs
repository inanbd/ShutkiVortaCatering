namespace ShutkiVorta.Infrastructure.Persistence.Seed;

/// <summary>Bound from the "Seed" section. Creates the first administrator, the starter menu and the starter inventory.</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public bool SeedMenu { get; set; } = true;

    /// <summary>Common ingredient and supply names plus three sample purchases, added once when the inventory tables are created.</summary>
    public bool SeedInventory { get; set; } = true;

    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public string AdminName { get; set; } = "Administrator";
}
