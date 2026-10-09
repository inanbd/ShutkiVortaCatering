using FluentValidation;
using MediatR;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Domain.Common;
using ShutkiVorta.Domain.Menu;
using ValidationException = ShutkiVorta.Application.Common.Exceptions.ValidationException;

namespace ShutkiVorta.Application.Features.Menu;

/// <summary>Creates a menu item (when <see cref="Id"/> is null) or updates an existing one. Returns the item id.</summary>
public sealed record SaveMenuItemCommand : IRequest<int>, IRequireAdmin
{
    public int? Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? BengaliName { get; init; }
    public string? Slug { get; init; }
    public MenuCategory Category { get; init; } = MenuCategory.ClassicVorta;
    public string ShortDescription { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? Ingredients { get; init; }
    public decimal PricePerUnit { get; init; }

    /// <summary>Agreed price per unit for restaurant orders; null uses the retail price less Wholesale:DiscountPercent.</summary>
    public decimal? WholesalePricePerUnit { get; init; }
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

    internal MenuItemDetails ToDetails() => new()
    {
        Name = Name,
        BengaliName = BengaliName,
        Slug = Slug,
        Category = Category,
        ShortDescription = ShortDescription,
        Description = Description,
        Ingredients = Ingredients,
        PricePerUnit = PricePerUnit,
        WholesalePricePerUnit = WholesalePricePerUnit,
        Unit = Unit,
        MinimumQuantity = MinimumQuantity,
        QuantityStep = QuantityStep,
        SpiceLevel = SpiceLevel,
        ImageUrl = ImageUrl,
        ImageAlt = ImageAlt,
        ImageCredit = ImageCredit,
        IsAvailable = IsAvailable,
        IsFeatured = IsFeatured,
        SortOrder = SortOrder,
        MetaTitle = MetaTitle,
        MetaDescription = MetaDescription,
    };
}

public sealed class SaveMenuItemCommandValidator : AbstractValidator<SaveMenuItemCommand>
{
    public SaveMenuItemCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.BengaliName).MaximumLength(120);
        RuleFor(x => x.Slug)
            .MaximumLength(SlugGenerator.MaxLength)
            .Must(s => string.IsNullOrWhiteSpace(s) || SlugGenerator.IsValid(s))
            .WithMessage("Slug may only contain lowercase letters, numbers and single hyphens (e.g. \"loitta-shutki-vorta\").");
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.ShortDescription).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Ingredients).MaximumLength(1000);
        RuleFor(x => x.PricePerUnit).GreaterThan(0).LessThan(10000);
        RuleFor(x => x.WholesalePricePerUnit).GreaterThan(0).LessThan(10000).When(x => x.WholesalePricePerUnit.HasValue);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(20);
        RuleFor(x => x.MinimumQuantity).GreaterThan(0).LessThanOrEqualTo(100);
        RuleFor(x => x.QuantityStep).GreaterThan(0).LessThanOrEqualTo(100);
        RuleFor(x => x.SpiceLevel).InclusiveBetween(0, MenuItem.MaxSpiceLevel);
        RuleFor(x => x.ImageUrl).MaximumLength(500)
            .Must(u => string.IsNullOrWhiteSpace(u) || u.StartsWith('/') || Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            .WithMessage("Image URL must be a site path (starting with /) or an http(s) address.");
        RuleFor(x => x.ImageAlt).MaximumLength(200);
        RuleFor(x => x.ImageCredit).MaximumLength(300);
        RuleFor(x => x.MetaTitle).MaximumLength(70);
        RuleFor(x => x.MetaDescription).MaximumLength(170);
    }
}

internal sealed class SaveMenuItemCommandHandler(
    IMenuItemRepository repository,
    IDateTimeProvider clock,
    IPublisher publisher) : IRequestHandler<SaveMenuItemCommand, int>
{
    public async Task<int> Handle(SaveMenuItemCommand request, CancellationToken cancellationToken)
    {
        var details = request.ToDetails();
        var slug = SlugGenerator.Generate(string.IsNullOrWhiteSpace(details.Slug) ? details.Name : details.Slug);

        if (await repository.SlugExistsAsync(slug, request.Id, cancellationToken))
        {
            throw new ValidationException(nameof(SaveMenuItemCommand.Slug), $"Another menu item already uses the URL \"/menu/{slug}\". Please choose a different slug.");
        }

        MenuItem item;
        try
        {
            if (request.Id is { } id)
            {
                item = await repository.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("Menu item", id);
                item.Update(details, clock.UtcNow);
                await repository.UpdateAsync(item, cancellationToken);
            }
            else
            {
                item = MenuItem.Create(details, clock.UtcNow);
                await repository.AddAsync(item, cancellationToken);
            }
        }
        catch (DomainException ex)
        {
            throw new ValidationException(ex.Message);
        }

        await publisher.Publish(new MenuChangedNotification(item.Id), cancellationToken);
        return item.Id;
    }
}
