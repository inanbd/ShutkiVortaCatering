using System.ComponentModel.DataAnnotations;
using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Features.Wholesale;
using ShutkiVorta.Domain.Wholesale;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Recurring;

public sealed class DetailsModel(ISender sender) : AppPageModel(sender)
{
    public StandingOrderDetailsDto Order { get; private set; } = null!;
    public WholesaleCatalogDto Catalog { get; private set; } = null!;

    [BindProperty]
    public TermsInput Input { get; set; } = new();

    public bool CanApprove => Order.Status is StandingOrderStatus.PendingApproval or StandingOrderStatus.Declined;
    public bool CanDecline => Order.Status == StandingOrderStatus.PendingApproval;
    public bool CanEditTerms => !Order.Status.IsFinal();
    public bool HasActions => CanApprove || CanDecline || Order.CanPause || Order.CanResume || Order.CanCancel;

    /// <summary>Catalog wholesale price per item, to compare agreed prices against.</summary>
    public decimal? CatalogPrice(int menuItemId) => Catalog.Items.FirstOrDefault(i => i.Id == menuItemId)?.WholesalePrice;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        Input = TermsInput.From(Order);
        return Page();
    }

    public async Task<IActionResult> OnPostStatusAsync(int id, StandingOrderAction change, string? note, bool notifyRestaurant, CancellationToken cancellationToken)
    {
        var message = string.Empty;
        var ok = await TryExecuteAsync(async () =>
            message = await Sender.Send(new ChangeStandingOrderStatusCommand(id, change, note, notifyRestaurant), cancellationToken));
        if (ok)
        {
            StatusMessage = message + (notifyRestaurant ? " The restaurant will be notified by email." : string.Empty);
        }
        else
        {
            ErrorMessage = Errors();
        }

        return Redirect($"/admin/recurring/{id}");
    }

    public async Task<IActionResult> OnPostSkipAsync(int id, DateOnly date, bool skip, CancellationToken cancellationToken)
    {
        var ok = await TryExecuteAsync(() => Sender.Send(new SkipStandingOrderDateCommand(id, date, skip), cancellationToken));
        var day = date.ToString("dddd, MMM d", Format.Culture);
        if (ok)
        {
            StatusMessage = skip
                ? $"The delivery on {day} was skipped. Any order already created for it was cancelled."
                : $"The delivery on {day} was restored.";
        }
        else
        {
            ErrorMessage = Errors();
        }

        return Redirect($"/admin/recurring/{id}");
    }

    public async Task<IActionResult> OnPostTermsAsync(int id, CancellationToken cancellationToken)
    {
        if (!TimeOnly.TryParseExact(Input.PreferredTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            ModelState.AddModelError("Input.PreferredTime", "Please choose a delivery time.");
        }

        for (var i = 0; i < Input.Lines.Count; i++)
        {
            if (!Input.Lines[i].Remove && Input.Lines[i].Quantity is null)
            {
                ModelState.AddModelError($"Input.Lines[{i}].Quantity", "Enter a quantity, or tick Remove.");
            }
        }

        if (Input.NewMenuItemId is not null && Input.NewQuantity is not > 0)
        {
            ModelState.AddModelError("Input.NewQuantity", "Enter the quantity for the item you are adding.");
        }

        if (ModelState.IsValid && await TryExecuteAsync(() => Sender.Send(Input.ToCommand(id, time), cancellationToken), typeof(TermsInput)))
        {
            StatusMessage = "Terms saved. Upcoming orders the kitchen has not started were re-issued with the new terms.";
            return Redirect($"/admin/recurring/{id}");
        }

        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        // Keep what the admin typed, but show the item names from the saved agreement.
        foreach (var line in Input.Lines)
        {
            var saved = Order.Lines.FirstOrDefault(l => l.MenuItemId == line.MenuItemId);
            line.ItemName ??= saved?.ItemName;
            line.Unit ??= saved?.Unit;
        }

        ViewData["TermsError"] = true;
        return Page();
    }

    private async Task<bool> LoadAsync(int id, CancellationToken cancellationToken)
    {
        var order = await Sender.Send(new GetStandingOrderQuery(id), cancellationToken);
        if (order is null)
        {
            return false;
        }

        Order = order;
        Catalog = await Sender.Send(new GetWholesaleCatalogQuery(), cancellationToken);
        ViewData["Title"] = order.BusinessName;
        return true;
    }

    private string Errors() => string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));

    public sealed class TermsInput
    {
        public List<TermsLineInput> Lines { get; set; } = [];

        [Display(Name = "Add a vorta")]
        public int? NewMenuItemId { get; set; }

        [Range(0, 1000)]
        [Display(Name = "Quantity")]
        public decimal? NewQuantity { get; set; }

        [Range(0, 10000)]
        [Display(Name = "Agreed price")]
        public decimal? NewUnitPrice { get; set; }

        public List<DayOfWeek> Days { get; set; } = [];

        [Display(Name = "Delivery time")]
        public string? PreferredTime { get; set; }

        [Display(Name = "First delivery")]
        public DateOnly StartDate { get; set; }

        [Display(Name = "Last delivery (optional)")]
        public DateOnly? EndDate { get; set; }

        [Range(0, 1000)]
        [Display(Name = "Delivery fee ($)")]
        public decimal DeliveryFee { get; set; }

        [Display(Name = "Tax exempt")]
        public bool TaxExempt { get; set; }

        [StringLength(2000)]
        [Display(Name = "Admin notes (private)")]
        public string? AdminNotes { get; set; }

        public static TermsInput From(StandingOrderDetailsDto so) => new()
        {
            Lines = so.Lines.Select(l => new TermsLineInput
            {
                MenuItemId = l.MenuItemId,
                ItemName = l.ItemName,
                Unit = l.Unit,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
            }).ToList(),
            Days = so.Days.ToDays().ToList(),
            PreferredTime = so.PreferredTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            StartDate = so.StartDate,
            EndDate = so.EndDate,
            DeliveryFee = so.DeliveryFee,
            TaxExempt = so.TaxExempt,
            AdminNotes = so.AdminNotes,
        };

        /// <summary>Lines left at zero or ticked "Remove" are dropped; a blank price means the catalog wholesale price.</summary>
        public UpdateStandingOrderTermsCommand ToCommand(int id, TimeOnly preferredTime)
        {
            var lines = Lines
                .Where(l => !l.Remove && l.Quantity > 0)
                .Select(l => new StandingOrderTermsLine(l.MenuItemId, l.Quantity!.Value, l.UnitPrice ?? 0m))
                .ToList();

            if (NewMenuItemId is { } newItem && NewQuantity is > 0)
            {
                lines.Add(new StandingOrderTermsLine(newItem, NewQuantity.Value, NewUnitPrice ?? 0m));
            }

            return new UpdateStandingOrderTermsCommand
            {
                Id = id,
                Lines = lines,
                Days = Days.Distinct().ToList(),
                PreferredTime = preferredTime,
                StartDate = StartDate,
                EndDate = EndDate,
                DeliveryFee = DeliveryFee,
                TaxExempt = TaxExempt,
                AdminNotes = AdminNotes,
            };
        }
    }

    public sealed class TermsLineInput
    {
        public int MenuItemId { get; set; }
        public string? ItemName { get; set; }
        public string? Unit { get; set; }

        [Range(0, 1000)]
        public decimal? Quantity { get; set; }

        [Range(0, 10000)]
        public decimal? UnitPrice { get; set; }

        public bool Remove { get; set; }
    }
}
