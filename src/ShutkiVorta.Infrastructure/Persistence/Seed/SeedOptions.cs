namespace ShutkiVorta.Infrastructure.Persistence.Seed;

/// <summary>Bound from the "Seed" section. Creates the first administrator and the starter menu.</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public bool SeedMenu { get; set; } = true;
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public string AdminName { get; set; } = "Administrator";
}
