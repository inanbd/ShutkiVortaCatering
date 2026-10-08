using ShutkiVorta.Domain.Menu;

namespace ShutkiVorta.Application.Features.Menu;

public sealed record MenuItemDto
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string? BengaliName { get; init; }
    public required string Slug { get; init; }
    public MenuCategory Category { get; init; }
    public required string CategoryName { get; init; }
    public required string CategoryBengaliName { get; init; }
    public required string ShortDescription { get; init; }
    public required string Description { get; init; }
    public string? Ingredients { get; init; }
    public IReadOnlyList<string> IngredientList { get; init; } = [];
    public decimal PricePerUnit { get; init; }
    public required string Unit { get; init; }
    public decimal MinimumQuantity { get; init; }
    public decimal QuantityStep { get; init; }
    public int SpiceLevel { get; init; }
    public string? ImageUrl { get; init; }
    public string? ImageAlt { get; init; }
    public string? ImageCredit { get; init; }
    public bool IsAvailable { get; init; }
    public bool IsFeatured { get; init; }
    public int SortOrder { get; init; }
    public string? MetaTitle { get; init; }
    public string? MetaDescription { get; init; }
    public DateTime UpdatedAtUtc { get; init; }

    public string DisplayImageAlt => ImageAlt ?? $"{Name} — Bangladeshi vorta";
}

public static class MenuCategoryNames
{
    public static string English(MenuCategory category) => category switch
    {
        MenuCategory.ShutkiVorta => "Shutki Vorta",
        MenuCategory.ClassicVorta => "Classic Vorta",
        _ => category.ToString(),
    };

    public static string Bengali(MenuCategory category) => category switch
    {
        MenuCategory.ShutkiVorta => "শুঁটকি ভর্তা",
        MenuCategory.ClassicVorta => "ঘরোয়া ভর্তা",
        _ => string.Empty,
    };
}

public static class MenuItemMapping
{
    public static MenuItemDto ToDto(this MenuItem item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        BengaliName = item.BengaliName,
        Slug = item.Slug,
        Category = item.Category,
        CategoryName = MenuCategoryNames.English(item.Category),
        CategoryBengaliName = MenuCategoryNames.Bengali(item.Category),
        ShortDescription = item.ShortDescription,
        Description = item.Description,
        Ingredients = item.Ingredients,
        IngredientList = string.IsNullOrWhiteSpace(item.Ingredients)
            ? []
            : item.Ingredients.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        PricePerUnit = item.PricePerUnit,
        Unit = item.Unit,
        MinimumQuantity = item.MinimumQuantity,
        QuantityStep = item.QuantityStep,
        SpiceLevel = item.SpiceLevel,
        ImageUrl = item.ImageUrl,
        ImageAlt = item.ImageAlt,
        ImageCredit = item.ImageCredit,
        IsAvailable = item.IsAvailable,
        IsFeatured = item.IsFeatured,
        SortOrder = item.SortOrder,
        MetaTitle = item.MetaTitle,
        MetaDescription = item.MetaDescription,
        UpdatedAtUtc = item.UpdatedAtUtc,
    };
}
