using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Domain.Menu;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Menu;

public sealed class EditModel(ISender sender) : AppPageModel(sender)
{
    [BindProperty]
    public MenuItemInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? ImageFile { get; set; }

    public bool IsNew => Input.Id is null;

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken cancellationToken)
    {
        if (id is { } existingId)
        {
            var item = await Sender.Send(new GetMenuItemByIdQuery(existingId), cancellationToken);
            if (item is null)
            {
                return NotFound();
            }

            Input = MenuItemInput.From(item);
        }

        ViewData["Title"] = IsNew ? "New menu item" : $"Edit {Input.Name}";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id, CancellationToken cancellationToken)
    {
        Input.Id = id;
        ViewData["Title"] = IsNew ? "New menu item" : $"Edit {Input.Name}";
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (ImageFile is { Length: > 0 })
        {
            await using var buffer = new MemoryStream();
            await ImageFile.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;

            var uploaded = await TryExecuteAsync(async () =>
                Input.ImageUrl = await Sender.Send(new UploadMenuImageCommand(buffer, ImageFile.FileName, ImageFile.Length), cancellationToken));
            if (!uploaded)
            {
                return Page();
            }
        }

        int savedId = 0;
        var ok = await TryExecuteAsync(async () => savedId = await Sender.Send(Input.ToCommand(), cancellationToken), typeof(MenuItemInput));
        if (!ok)
        {
            return Page();
        }

        StatusMessage = $"\"{Input.Name}\" saved. Its page, the sitemap and robots.txt are up to date.";
        return Redirect($"/admin/menu/edit/{savedId}");
    }

    public sealed class MenuItemInput
    {
        public int? Id { get; set; }

        [Required, StringLength(120)]
        public string Name { get; set; } = string.Empty;

        [StringLength(120)]
        [Display(Name = "Bengali name")]
        public string? BengaliName { get; set; }

        [StringLength(120)]
        [Display(Name = "URL slug")]
        public string? Slug { get; set; }

        public MenuCategory Category { get; set; } = MenuCategory.ClassicVorta;

        [Required, StringLength(300)]
        [Display(Name = "Short description")]
        public string ShortDescription { get; set; } = string.Empty;

        [Required, StringLength(4000)]
        [Display(Name = "Full description")]
        public string Description { get; set; } = string.Empty;

        [StringLength(1000)]
        [Display(Name = "Ingredients (comma separated)")]
        public string? Ingredients { get; set; }

        [Range(0.01, 9999)]
        [Display(Name = "Price per unit ($)")]
        public decimal PricePerUnit { get; set; } = 12.99m;

        [Required, StringLength(20)]
        public string Unit { get; set; } = MenuItem.DefaultUnit;

        [Range(0.01, 100)]
        [Display(Name = "Minimum quantity")]
        public decimal MinimumQuantity { get; set; } = 0.5m;

        [Range(0.01, 100)]
        [Display(Name = "Quantity step")]
        public decimal QuantityStep { get; set; } = 0.5m;

        [Range(0, 5)]
        [Display(Name = "Spice level (0–5)")]
        public int SpiceLevel { get; set; } = 2;

        [StringLength(500)]
        [Display(Name = "Image URL")]
        public string? ImageUrl { get; set; }

        [StringLength(200)]
        [Display(Name = "Image alt text")]
        public string? ImageAlt { get; set; }

        [StringLength(300)]
        [Display(Name = "Image credit")]
        public string? ImageCredit { get; set; }

        [Display(Name = "Available to order")]
        public bool IsAvailable { get; set; } = true;

        [Display(Name = "Featured")]
        public bool IsFeatured { get; set; }

        [Display(Name = "Sort order")]
        public int SortOrder { get; set; }

        [StringLength(70)]
        [Display(Name = "SEO title")]
        public string? MetaTitle { get; set; }

        [StringLength(170)]
        [Display(Name = "SEO description")]
        public string? MetaDescription { get; set; }

        public static MenuItemInput From(MenuItemDto d) => new()
        {
            Id = d.Id,
            Name = d.Name,
            BengaliName = d.BengaliName,
            Slug = d.Slug,
            Category = d.Category,
            ShortDescription = d.ShortDescription,
            Description = d.Description,
            Ingredients = d.Ingredients,
            PricePerUnit = d.PricePerUnit,
            Unit = d.Unit,
            MinimumQuantity = d.MinimumQuantity,
            QuantityStep = d.QuantityStep,
            SpiceLevel = d.SpiceLevel,
            ImageUrl = d.ImageUrl,
            ImageAlt = d.ImageAlt,
            ImageCredit = d.ImageCredit,
            IsAvailable = d.IsAvailable,
            IsFeatured = d.IsFeatured,
            SortOrder = d.SortOrder,
            MetaTitle = d.MetaTitle,
            MetaDescription = d.MetaDescription,
        };

        public SaveMenuItemCommand ToCommand() => new()
        {
            Id = Id,
            Name = Name,
            BengaliName = BengaliName,
            Slug = Slug,
            Category = Category,
            ShortDescription = ShortDescription,
            Description = Description,
            Ingredients = Ingredients,
            PricePerUnit = PricePerUnit,
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
}
