using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Features.Inventory;
using ShutkiVorta.Domain.Inventory;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Inventory;

public sealed class ItemModel(ISender sender) : AppPageModel(sender)
{
    [BindProperty]
    public ItemInput Input { get; set; } = new();

    public InventoryItemDetailsDto? Details { get; private set; }
    public IReadOnlyList<string> Units { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        Input = new ItemInput { Name = Details!.Summary.Name, Unit = Details.Summary.Unit };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid
            && await TryExecuteAsync(() => Sender.Send(new SaveInventoryItemCommand(id, Input.Name, Input.Unit), cancellationToken), typeof(ItemInput)))
        {
            StatusMessage = "Item saved. Every purchase shows the new name.";
            return Redirect($"/admin/inventory/items/{id}");
        }

        return await LoadAsync(id, cancellationToken) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        if (await TryExecuteAsync(() => Sender.Send(new DeleteInventoryItemCommand(id), cancellationToken)))
        {
            StatusMessage = "Item deleted. It will no longer be suggested.";
            return Redirect("/admin/inventory/items");
        }

        ErrorMessage = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        return Redirect($"/admin/inventory/items/{id}");
    }

    private async Task<bool> LoadAsync(int id, CancellationToken cancellationToken)
    {
        Details = await Sender.Send(new GetInventoryItemQuery(id), cancellationToken);
        if (Details is null)
        {
            return false;
        }

        ViewData["Title"] = Details.Summary.Name;
        Units = (await Sender.Send(new GetInventoryFormOptionsQuery(), cancellationToken)).Units;
        return true;
    }

    public sealed class ItemInput
    {
        [Required, StringLength(InventoryItem.MaxNameLength)]
        public string Name { get; set; } = string.Empty;

        [Required, StringLength(InventoryItem.MaxUnitLength)]
        [Display(Name = "Usual unit")]
        public string Unit { get; set; } = InventoryItem.DefaultUnit;
    }
}
