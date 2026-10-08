using ShutkiVorta.Domain.Common;

namespace ShutkiVorta.Domain.Menu;

/// <summary>A dish on the menu, sold by weight (pounds by default).</summary>
public sealed class MenuItem : Entity
{
    public const string DefaultUnit = "lb";
    public const int MaxSpiceLevel = 5;

    // Required by the data mapper.
    private MenuItem()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public string? BengaliName { get; private set; }
    public string Slug { get; private set; } = string.Empty;
    public MenuCategory Category { get; private set; }
    public string ShortDescription { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string? Ingredients { get; private set; }
    public decimal PricePerUnit { get; private set; }
    public decimal? WholesalePricePerUnit { get; private set; }
    public string Unit { get; private set; } = DefaultUnit;
    public decimal MinimumQuantity { get; private set; }
    public decimal QuantityStep { get; private set; }
    public int SpiceLevel { get; private set; }
    public string? ImageUrl { get; private set; }
    public string? ImageAlt { get; private set; }
    public string? ImageCredit { get; private set; }
    public bool IsAvailable { get; private set; }
    public bool IsFeatured { get; private set; }
    public int SortOrder { get; private set; }
    public string? MetaTitle { get; private set; }
    public string? MetaDescription { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static MenuItem Create(MenuItemDetails details, DateTime nowUtc)
    {
        var item = new MenuItem { CreatedAtUtc = nowUtc };
        item.Apply(details, nowUtc);
        return item;
    }

    public void Update(MenuItemDetails details, DateTime nowUtc) => Apply(details, nowUtc);

    public void SetAvailability(bool isAvailable, DateTime nowUtc)
    {
        IsAvailable = isAvailable;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Returns a customer-facing error if <paramref name="quantity"/> cannot be ordered, otherwise <c>null</c>.</summary>
    public string? ValidateQuantity(decimal quantity)
    {
        if (quantity < MinimumQuantity)
        {
            return $"{Name}: the minimum order is {FormatQuantity(MinimumQuantity)}.";
        }

        if (QuantityStep > 0 && (quantity - MinimumQuantity) % QuantityStep != 0)
        {
            return $"{Name}: please order in steps of {FormatQuantity(QuantityStep)}.";
        }

        return null;
    }

    public string FormatQuantity(decimal quantity) => $"{quantity:0.##} {Unit}";

    /// <summary>Price per unit for restaurants: the explicit wholesale price, or retail less the default discount.</summary>
    public decimal EffectiveWholesalePrice(decimal defaultDiscountPercent) =>
        WholesalePricePerUnit ?? Money.Round(PricePerUnit * (1 - Math.Clamp(defaultDiscountPercent, 0, 90) / 100m));

    private void Apply(MenuItemDetails d, DateTime nowUtc)
    {
        Name = Guard.NotEmpty(d.Name, "Name", 120);
        BengaliName = Guard.Optional(d.BengaliName, "Bengali name", 120);

        var slug = SlugGenerator.Generate(string.IsNullOrWhiteSpace(d.Slug) ? d.Name : d.Slug);
        if (slug.Length == 0)
        {
            throw new DomainException("A URL slug could not be generated. Please provide a slug using English letters or numbers.");
        }

        Slug = slug;
        Category = Enum.IsDefined(d.Category) ? d.Category : throw new DomainException("Unknown menu category.");
        ShortDescription = Guard.NotEmpty(d.ShortDescription, "Short description", 300);
        Description = Guard.NotEmpty(d.Description, "Description", 4000);
        Ingredients = Guard.Optional(d.Ingredients, "Ingredients", 1000);
        PricePerUnit = Money.Round(Guard.Positive(d.PricePerUnit, "Price"));
        WholesalePricePerUnit = d.WholesalePricePerUnit is { } wholesale ? Money.Round(Guard.Positive(wholesale, "Wholesale price")) : null;
        Unit = Guard.NotEmpty(d.Unit, "Unit", 20);
        MinimumQuantity = Guard.Positive(d.MinimumQuantity, "Minimum quantity");
        QuantityStep = Guard.Positive(d.QuantityStep, "Quantity step");
        SpiceLevel = d.SpiceLevel is >= 0 and <= MaxSpiceLevel
            ? d.SpiceLevel
            : throw new DomainException($"Spice level must be between 0 and {MaxSpiceLevel}.");
        ImageUrl = Guard.Optional(d.ImageUrl, "Image URL", 500);
        ImageAlt = Guard.Optional(d.ImageAlt, "Image alt text", 200);
        ImageCredit = Guard.Optional(d.ImageCredit, "Image credit", 300);
        IsAvailable = d.IsAvailable;
        IsFeatured = d.IsFeatured;
        SortOrder = d.SortOrder;
        MetaTitle = Guard.Optional(d.MetaTitle, "Meta title", 70);
        MetaDescription = Guard.Optional(d.MetaDescription, "Meta description", 170);
        UpdatedAtUtc = nowUtc;
    }
}
