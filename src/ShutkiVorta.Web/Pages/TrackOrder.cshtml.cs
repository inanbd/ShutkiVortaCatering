using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Web.Infrastructure;
using ShutkiVorta.Web.Seo;

namespace ShutkiVorta.Web.Pages;

public sealed class TrackOrderModel(ISender sender) : AppPageModel(sender)
{
    [BindProperty]
    public TrackInput Input { get; set; } = new();

    public void OnGet(string? orderNumber)
    {
        ViewData.SetSeo(new SeoMetadata { Title = "Track your order", NoIndex = true, Description = "Check the status of your Shutki Vorta Catering order." });
        Input.OrderNumber = orderNumber ?? string.Empty;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ViewData.SetSeo(new SeoMetadata { Title = "Track your order", NoIndex = true });
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var number = Input.OrderNumber.Trim().ToUpperInvariant();
        var token = await Sender.Send(new FindGuestOrderQuery(number, Input.Email), cancellationToken);
        if (token is null)
        {
            ModelState.AddModelError(string.Empty, "We couldn't find an order with that number and email address. Please check both and try again.");
            return Page();
        }

        return Redirect($"/order/{Uri.EscapeDataString(number)}?token={Uri.EscapeDataString(token)}");
    }

    public sealed class TrackInput
    {
        [Required(ErrorMessage = "Please enter your order number.")]
        [Display(Name = "Order number")]
        public string OrderNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter the email used for the order.")]
        [EmailAddress]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;
    }
}
