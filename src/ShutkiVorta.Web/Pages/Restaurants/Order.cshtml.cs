using System.ComponentModel.DataAnnotations;
using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Application.Features.Wholesale;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.Web.Infrastructure;
using ShutkiVorta.Web.Seo;

namespace ShutkiVorta.Web.Pages.Restaurants;

/// <summary>Restaurants request a standing (recurring) order: vortas by the pound on chosen days of the week.</summary>
public sealed class OrderModel(ISender sender, ICurrentUser currentUser) : AppPageModel(sender)
{
    [BindProperty]
    public RestaurantOrderInput Input { get; set; } = new();

    public WholesaleCatalogDto Catalog { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);

        if (await Sender.Send(new GetMyProfileQuery(), cancellationToken) is { } profile)
        {
            Input.ContactName = profile.FullName;
            Input.Email = profile.Email;
            Input.Phone = profile.PhoneNumber ?? string.Empty;
            Input.AddressLine1 = profile.AddressLine1;
            Input.AddressLine2 = profile.AddressLine2;
            Input.City = string.IsNullOrWhiteSpace(profile.City) ? Input.City : profile.City;
            Input.State = string.IsNullOrWhiteSpace(profile.State) ? Input.State : profile.State;
            Input.PostalCode = profile.PostalCode;
        }

        Input.Email = string.IsNullOrEmpty(Input.Email) ? currentUser.Email ?? string.Empty : Input.Email;
        Input.StartDate = Catalog.EarliestStartDate;
        Input.PreferredTime = Catalog.TimeSlots.FirstOrDefault(t => t.Hour >= 10).ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);

        if (!TimeOnly.TryParseExact(Input.PreferredTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            ModelState.AddModelError("Input.PreferredTime", "Please choose a delivery time.");
        }

        if (Input.StartDate is null)
        {
            ModelState.AddModelError("Input.StartDate", "Please choose the date of your first delivery.");
        }

        var items = Input.Quantities
            .Where(q => q.Value is > 0)
            .Select(q => new StandingOrderItemInput(q.Key, q.Value!.Value))
            .ToList();
        if (items.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Please enter the pounds you need for at least one vorta.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        string? reference = null;
        var ok = await TryExecuteAsync(async () => reference = await Sender.Send(new SubmitStandingOrderCommand
        {
            BusinessName = Input.BusinessName,
            ContactName = Input.ContactName,
            Email = Input.Email,
            Phone = Input.Phone,
            TaxPermitNumber = Input.TaxPermitNumber,
            Fulfillment = Input.Fulfillment,
            AddressLine1 = Input.AddressLine1,
            AddressLine2 = Input.AddressLine2,
            City = Input.City,
            State = Input.State,
            PostalCode = Input.PostalCode,
            Days = Input.Days,
            PreferredTime = time,
            StartDate = Input.StartDate!.Value,
            EndDate = Input.EndDate,
            Notes = Input.Notes,
            Items = items,
        }, cancellationToken), typeof(RestaurantOrderInput));

        if (!ok || reference is null)
        {
            return Page();
        }

        return Redirect($"/account/restaurant-orders/{Uri.EscapeDataString(reference)}?submitted=true");
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Catalog = await Sender.Send(new GetWholesaleCatalogQuery(), cancellationToken);
        ViewData.SetSeo(new SeoMetadata
        {
            Title = "Start a Restaurant Standing Order",
            Description = "Request recurring wholesale deliveries of Bangladeshi vortas for your restaurant.",
            CanonicalPath = "/restaurants/order",
            NoIndex = true,
        });
    }
}

public sealed class RestaurantOrderInput
{
    [Required(ErrorMessage = "Please enter your restaurant's name.")]
    [StringLength(150)]
    [Display(Name = "Restaurant / business name")]
    public string BusinessName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter a contact name.")]
    [StringLength(120)]
    [Display(Name = "Contact person")]
    public string ContactName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter an email address.")]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter a phone number.")]
    [Phone]
    [Display(Name = "Phone")]
    public string Phone { get; set; } = string.Empty;

    [StringLength(32)]
    [Display(Name = "Texas sales tax permit no. (optional)")]
    public string? TaxPermitNumber { get; set; }

    /// <summary>Pounds per delivery, keyed by menu item id.</summary>
    public Dictionary<int, decimal?> Quantities { get; set; } = [];

    [Display(Name = "Delivery days")]
    public List<DayOfWeek> Days { get; set; } = [];

    [Display(Name = "Delivery time")]
    public string PreferredTime { get; set; } = string.Empty;

    [Display(Name = "First delivery")]
    public DateOnly? StartDate { get; set; }

    [Display(Name = "Last delivery (optional)")]
    public DateOnly? EndDate { get; set; }

    public FulfillmentMethod Fulfillment { get; set; } = FulfillmentMethod.Delivery;

    [Display(Name = "Street address")]
    public string? AddressLine1 { get; set; }

    [Display(Name = "Suite / unit")]
    public string? AddressLine2 { get; set; }

    [Display(Name = "City")]
    public string? City { get; set; } = "Dallas";

    [Display(Name = "State")]
    public string? State { get; set; } = "TX";

    [Display(Name = "ZIP code")]
    public string? PostalCode { get; set; }

    [StringLength(1000)]
    [Display(Name = "Notes for our kitchen")]
    public string? Notes { get; set; }
}
