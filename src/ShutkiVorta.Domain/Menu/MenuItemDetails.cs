namespace ShutkiVorta.Domain.Menu;

/// <summary>Editable attributes of a menu item, used to create or update one in a single, validated step.</summary>
public sealed record MenuItemDetails
{
    public required string Name { get; init; }
    public string? BengaliName { get; init; }
    public string? Slug { get; init; }
    public MenuCategory Category { get; init; } = MenuCategory.ClassicVorta;
    public required string ShortDescription { get; init; }
    public required string Description { get; init; }
    public string? Ingredients { get; init; }
    public decimal PricePerUnit { get; init; }
    public string Unit { get; init; } = MenuItem.DefaultUnit;
    public decimal MinimumQuantity { get; init; } = 0.5m;
    public decimal QuantityStep { get; init; } = 0.5m;
    public int SpiceLevel { get; init; } = 2;
    public string? ImageUrl { get; init; }
    public string? ImageAlt { get; init; }
    public string? ImageCredit { get; init; }
    public bool IsAvailable { get; init; } = true;
    public bool IsFeatured { get; init; }
    public int SortOrder { get; init; }
    public string? MetaTitle { get; init; }
    public string? MetaDescription { get; init; }
}
