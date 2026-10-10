using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Features.Inventory;
using ShutkiVorta.Domain.Inventory;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Inventory;

public sealed class ItemsModel(ISender sender) : AppPageModel(sender)
{
    [BindProperty]
    public NewItemInput NewItem { get; set; } = new();

    public IReadOnlyList<InventoryItemSummaryDto> Items { get; private set; } = [];
    public IReadOnlyList<string> Units { get; private set; } = [];

    public Task OnGetAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken)
    {
        if (ModelState.IsValid
            && await TryExecuteAsync(() => Sender.Send(new SaveInventoryItemCommand(null, NewItem.Name, NewItem.Unit), cancellationToken), typeof(NewItemInput), nameof(NewItem)))
        {
            StatusMessage = $"\"{NewItem.Name.Trim()}\" saved. It will be suggested when you add to the inventory.";
            return RedirectToPage();
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Inventory";
        Items = await Sender.Send(new GetInventoryItemsQuery(), cancellationToken);
        Units = (await Sender.Send(new GetInventoryFormOptionsQuery(), cancellationToken)).Units;
    }

    public sealed class NewItemInput
    {
        [Required, StringLength(InventoryItem.MaxNameLength)]
        [Display(Name = "Item name")]
        public string Name { get; set; } = string.Empty;

        [Required, StringLength(InventoryItem.MaxUnitLength)]
        [Display(Name = "Usual unit")]
        public string Unit { get; set; } = InventoryItem.DefaultUnit;
    }
}
