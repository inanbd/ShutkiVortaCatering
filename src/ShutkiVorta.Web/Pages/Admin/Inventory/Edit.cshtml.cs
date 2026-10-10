using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Features.Inventory;
using ShutkiVorta.Domain.Inventory;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Inventory;

[RequestSizeLimit(MaxRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
public sealed class EditModel(ISender sender, IDateTimeProvider clock) : AppPageModel(sender)
{
    // Room for every receipt photo a purchase can have at the largest allowed size, plus the form fields.
    private const long MaxRequestBytes = InventoryPurchase.MaxReceipts * SaveInventoryPurchaseCommand.MaxReceiptBytes + 1024 * 1024;

    private const int BlankRowsForNewPurchase = 3;

    [BindProperty]
    public PurchaseInput Input { get; set; } = new();

    [BindProperty]
    public List<IFormFile> ReceiptFiles { get; set; } = [];

    public InventoryFormOptionsDto Options { get; private set; } = new([], []);

    /// <summary>The saved purchase (null while adding a new one).</summary>
    public InventoryPurchaseDto? Purchase { get; private set; }

    /// <summary>Photos chosen before a failed save are not kept by the browser, so the admin is asked to choose them again.</summary>
    public bool ReceiptsNeedChoosingAgain { get; private set; }

    public bool IsNew => Input.Id is null;

    /// <summary>Today at the business, the latest date a purchase can have.</summary>
    public DateOnly Today => DateOnly.FromDateTime(clock.BusinessNow);

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken cancellationToken)
    {
        if (id is { } existingId)
        {
            Purchase = await Sender.Send(new GetInventoryPurchaseQuery(existingId), cancellationToken);
            if (Purchase is null)
            {
                return NotFound();
            }

            Input = PurchaseInput.From(Purchase);
            Input.Lines.Add(new LineInput());
        }
        else
        {
            Input.PurchasedOn = Today;
            Input.Lines.AddRange(Enumerable.Range(0, BlankRowsForNewPurchase).Select(_ => new LineInput()));
        }

        return await ShowAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(int? id, CancellationToken cancellationToken)
    {
        Input.Id = id;
        var uploads = ReceiptFiles.Where(f => f.Length > 0).ToList();
        if (!ModelState.IsValid)
        {
            ReceiptsNeedChoosingAgain = uploads.Count > 0;
            return await ShowAsync(cancellationToken);
        }

        // A blank unit for a saved item means its usual unit (the form fills it in too, when scripts run).
        Options = await Sender.Send(new GetInventoryFormOptionsQuery(), cancellationToken);
        var usualUnits = Options.Items
            .GroupBy(i => InventoryKey(i.Name), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Unit, StringComparer.Ordinal);

        var streams = uploads.Select(f => new ReceiptUpload(f.OpenReadStream(), f.FileName, f.Length)).ToList();
        var savedId = 0;
        bool ok;
        try
        {
            ok = await TryExecuteAsync(async () => savedId = await Sender.Send(Input.ToCommand(streams, usualUnits), cancellationToken), typeof(PurchaseInput));
        }
        finally
        {
            foreach (var upload in streams)
            {
                await upload.Content.DisposeAsync();
            }
        }

        if (!ok)
        {
            ReceiptsNeedChoosingAgain = uploads.Count > 0;
            return await ShowAsync(cancellationToken);
        }

        var lines = Input.FilledLines.ToList();
        var total = lines.Sum(l => l.Price ?? 0m);
        StatusMessage = $"{(IsNew ? "Added" : "Saved")} {lines.Count} item{(lines.Count == 1 ? string.Empty : "s")} bought on " +
                        $"{Format.ShortDate(Input.PurchasedOn.ToDateTime(TimeOnly.MinValue))}, {Format.Currency(total)} in total.";
        return Redirect($"/admin/inventory#purchase-{savedId}");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        if (await TryExecuteAsync(() => Sender.Send(new DeleteInventoryPurchaseCommand(id), cancellationToken)))
        {
            StatusMessage = "Purchase and its receipt photos deleted.";
            return Redirect("/admin/inventory");
        }

        ErrorMessage = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        return Redirect($"/admin/inventory/edit/{id}");
    }

    /// <summary>Same matching as saved item names: ignores case and extra spaces.</summary>
    internal static string InventoryKey(string? name) =>
        string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private async Task<IActionResult> ShowAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = IsNew ? "Add to inventory" : "Purchase";
        if (Options.Items.Count == 0)
        {
            Options = await Sender.Send(new GetInventoryFormOptionsQuery(), cancellationToken);
        }

        if (!IsNew && Purchase is null)
        {
            Purchase = await Sender.Send(new GetInventoryPurchaseQuery(Input.Id!.Value), cancellationToken);
            if (Purchase is null)
            {
                return NotFound();
            }
        }

        if (Input.Lines.Count == 0)
        {
            Input.Lines.Add(new LineInput());
        }

        return Page();
    }

    public sealed class PurchaseInput
    {
        public int? Id { get; set; }

        [Display(Name = "Date bought")]
        public DateOnly PurchasedOn { get; set; }

        [StringLength(120)]
        [Display(Name = "Store")]
        public string? Store { get; set; }

        [StringLength(1000)]
        [Display(Name = "Notes")]
        public string? Notes { get; set; }

        public List<LineInput> Lines { get; set; } = [];

        /// <summary>Receipts ticked "Remove".</summary>
        public List<int> RemoveReceiptIds { get; set; } = [];

        /// <summary>The rows the admin filled in; the spare blank rows of the form are ignored.</summary>
        public IEnumerable<LineInput> FilledLines => Lines.Where(l => !l.IsBlank);

        public static PurchaseInput From(InventoryPurchaseDto purchase) => new()
        {
            Id = purchase.Id,
            PurchasedOn = purchase.PurchasedOn,
            Store = purchase.Store,
            Notes = purchase.Notes,
            Lines = purchase.Lines.Select(l => new LineInput { ItemName = l.ItemName, Quantity = l.Quantity, Unit = l.Unit, Price = l.Price }).ToList(),
        };

        public SaveInventoryPurchaseCommand ToCommand(IReadOnlyList<ReceiptUpload> uploads, IReadOnlyDictionary<string, string> usualUnits) => new()
        {
            Id = Id,
            PurchasedOn = PurchasedOn,
            Store = Store,
            Notes = Notes,
            Lines = FilledLines
                .Select(l => new SaveInventoryPurchaseCommand.Line(
                    l.ItemName,
                    l.Quantity,
                    string.IsNullOrWhiteSpace(l.Unit) ? usualUnits.GetValueOrDefault(InventoryKey(l.ItemName)) : l.Unit,
                    l.Price))
                .ToList(),
            NewReceipts = uploads,
            RemoveReceiptIds = RemoveReceiptIds,
        };
    }

    public sealed class LineInput
    {
        public string? ItemName { get; set; }
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
        public decimal? Price { get; set; }

        /// <summary>No name, quantity or price. (The unit alone does not count: the form may have filled it in.)</summary>
        public bool IsBlank => string.IsNullOrWhiteSpace(ItemName) && Quantity is null && Price is null;
    }
}
