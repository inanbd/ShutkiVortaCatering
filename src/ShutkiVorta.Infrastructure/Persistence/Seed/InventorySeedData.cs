using ShutkiVorta.Domain.Inventory;

namespace ShutkiVorta.Infrastructure.Persistence.Seed;

/// <summary>
/// Starter inventory: the ingredients and supplies the starter menu needs, so they are suggested from the first purchase on,
/// plus three sample purchases that show how the page works. The samples say so in their notes.
/// </summary>
internal static class InventorySeedData
{
    public const string MigrationId = "0007_Inventory";
    public const string SampleNote = "Sample entry: delete it once you start recording your real purchases.";
    public const string SampleCreatedBy = "Sample data";

    public static IReadOnlyList<(string Name, string Unit)> Items =>
    [
        ("Loitta shutki", "lb"),
        ("Chingri shutki", "lb"),
        ("Chepa shutki", "lb"),
        ("Mustard oil", "bottle"),
        ("Red onion", "lb"),
        ("Shallots", "lb"),
        ("Garlic", "lb"),
        ("Green chili", "lb"),
        ("Dried red chili", "lb"),
        ("Cilantro", "bunch"),
        ("Potatoes", "lb"),
        ("Eggplant", "lb"),
        ("Tomatoes", "lb"),
        ("Masoor dal (red lentils)", "lb"),
        ("Kalojira (nigella seeds)", "lb"),
        ("Shim (flat beans)", "lb"),
        ("Limes", "piece"),
        ("Salt", "lb"),
        ("Deli containers 16 oz", "case"),
        ("Deli container lids", "case"),
        ("Food labels", "roll"),
        ("Nitrile gloves", "box"),
        ("Aluminum foil", "roll"),
    ];

    public static IReadOnlyList<SamplePurchase> Purchases =>
    [
        new(DaysAgo: 12, "Desi grocery",
        [
            new("Loitta shutki", 5m, "lb", 89.95m),
            new("Chingri shutki", 3m, "lb", 62.97m),
            new("Mustard oil", 4m, "bottle", 27.96m),
            new("Dried red chili", 2m, "lb", 13.98m),
        ]),
        new(DaysAgo: 6, "Farmers market",
        [
            new("Red onion", 20m, "lb", 15.80m),
            new("Garlic", 5m, "lb", 12.45m),
            new("Green chili", 3m, "lb", 8.97m),
            new("Cilantro", 10m, "bunch", 7.90m),
            new("Tomatoes", 10m, "lb", 14.90m),
            new("Eggplant", 8m, "lb", 11.92m),
        ]),
        new(DaysAgo: 2, "Restaurant supply store",
        [
            new("Deli containers 16 oz", 2m, "case", 79.98m),
            new("Deli container lids", 2m, "case", 39.98m),
            new("Nitrile gloves", 1m, "box", 12.99m),
        ]),
    ];

    internal sealed record SamplePurchase(int DaysAgo, string Store, IReadOnlyList<InventoryLineRequest> Lines);
}
