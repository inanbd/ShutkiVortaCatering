using System.ComponentModel.DataAnnotations;
using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.Web.Infrastructure;
using ShutkiVorta.Web.Services;

namespace ShutkiVorta.Web.Pages;

public sealed class CheckoutModel(ISender sender, CartService cart, ICurrentUser currentUser) : AppPageModel(sender)
{
    [BindProperty]
    public CheckoutInput Input { get; set; } = new();

    public CartQuoteDto Quote { get; private set; } = null!;
    public CheckoutOptionsDto Options { get; private set; } = null!;
    public bool IsSignedIn => currentUser.IsAuthenticated;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (await LoadAsync(cancellationToken) is { } redirect)
        {
            return redirect;
        }

        if (currentUser.IsAuthenticated && await Sender.Send(new GetMyProfileQuery(), cancellationToken) is { } profile)
        {
            Input.CustomerName = profile.FullName;
            Input.Email = profile.Email;
            Input.Phone = profile.PhoneNumber ?? string.Empty;
            Input.AddressLine1 = profile.AddressLine1;
            Input.AddressLine2 = profile.AddressLine2;
            Input.City = profile.City;
            Input.State = profile.State ?? "TX";
            Input.PostalCode = profile.PostalCode;
        }

        if (Options.Days.Count > 0)
        {
            Input.ScheduledDate = Options.Days[0].Value;
            Input.ScheduledTime = Options.Days[0].Slots[0].ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (await LoadAsync(cancellationToken) is { } redirect)
        {
            return redirect;
        }

        if (!DateOnly.TryParseExact(Input.ScheduledDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            ModelState.AddModelError("Input.ScheduledDate", "Please choose a date.");
        }

        if (!TimeOnly.TryParseExact(Input.ScheduledTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            ModelState.AddModelError("Input.ScheduledTime", "Please choose a time.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        PlaceOrderResult? result = null;
        var ok = await TryExecuteAsync(async () => result = await Sender.Send(new PlaceOrderCommand
        {
            CustomerName = Input.CustomerName,
            Email = Input.Email,
            Phone = Input.Phone,
            Fulfillment = Input.Fulfillment,
            AddressLine1 = Input.AddressLine1,
            AddressLine2 = Input.AddressLine2,
            City = Input.City,
            State = Input.State,
            PostalCode = Input.PostalCode,
            ScheduledDate = date,
            ScheduledTime = time,
            Notes = Input.Notes,
            Lines = cart.Lines,
        }, cancellationToken), typeof(CheckoutInput));

        if (!ok || result is null)
        {
            return Page();
        }

        cart.Clear();
        return Redirect($"/order/{Uri.EscapeDataString(result.OrderNumber)}/thank-you?token={Uri.EscapeDataString(result.TrackingToken)}");
    }

    private async Task<IActionResult?> LoadAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Checkout";
        if (cart.ItemCount == 0)
        {
            return Redirect("/cart");
        }

        Quote = await Sender.Send(new PriceCartQuery(cart.Lines), cancellationToken);
        if (Quote.IsEmpty || Quote.HasErrors || Quote.MissingItemIds.Count > 0)
        {
            ErrorMessage = Quote.IsEmpty ? null : "Please review your order — some items need attention.";
            return Redirect("/cart");
        }

        Options = await Sender.Send(new GetCheckoutOptionsQuery(), cancellationToken);
        return null;
    }
}

public sealed class CheckoutInput
{
    [Required(ErrorMessage = "Please enter your name.")]
    [StringLength(120)]
    [Display(Name = "Full name")]
    public string CustomerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter your email address.")]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter a phone number.")]
    [Phone]
    [Display(Name = "Mobile phone")]
    public string Phone { get; set; } = string.Empty;

    public FulfillmentMethod Fulfillment { get; set; } = FulfillmentMethod.Pickup;

    [Display(Name = "Street address")]
    public string? AddressLine1 { get; set; }

    [Display(Name = "Apt, suite (optional)")]
    public string? AddressLine2 { get; set; }

    public string? City { get; set; } = "Dallas";

    public string? State { get; set; } = "TX";

    [Display(Name = "ZIP code")]
    public string? PostalCode { get; set; }

    [Required(ErrorMessage = "Please choose a date.")]
    [Display(Name = "Date")]
    public string ScheduledDate { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please choose a time.")]
    [Display(Name = "Time")]
    public string ScheduledTime { get; set; } = string.Empty;

    [StringLength(1000)]
    [Display(Name = "Notes for our kitchen (optional)")]
    public string? Notes { get; set; }
}
