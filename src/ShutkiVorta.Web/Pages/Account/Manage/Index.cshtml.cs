using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Account.Manage;

public sealed class IndexModel(ISender sender) : AppPageModel(sender)
{
    [BindProperty]
    public ProfileInput Input { get; set; } = new();

    public string Email { get; private set; } = string.Empty;
    public bool EmailConfirmed { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "My profile";
        var profile = await Sender.Send(new GetMyProfileQuery(), cancellationToken);
        if (profile is null)
        {
            return Redirect("/account/login");
        }

        Email = profile.Email;
        EmailConfirmed = profile.EmailConfirmed;
        Input = new ProfileInput
        {
            FullName = profile.FullName,
            PhoneNumber = profile.PhoneNumber,
            AddressLine1 = profile.AddressLine1,
            AddressLine2 = profile.AddressLine2,
            City = profile.City,
            State = profile.State,
            PostalCode = profile.PostalCode,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "My profile";
        var profile = await Sender.Send(new GetMyProfileQuery(), cancellationToken);
        Email = profile?.Email ?? string.Empty;
        EmailConfirmed = profile?.EmailConfirmed ?? false;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var ok = await TryExecuteAsync(async () =>
        {
            var result = await Sender.Send(new UpdateMyProfileCommand(
                Input.FullName, Input.PhoneNumber, Input.AddressLine1, Input.AddressLine2, Input.City, Input.State, Input.PostalCode), cancellationToken);
            if (!result.Succeeded)
            {
                AddErrors(result.Errors);
            }
        }, typeof(ProfileInput));

        if (!ok || !ModelState.IsValid)
        {
            return Page();
        }

        StatusMessage = "Your profile has been saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResendConfirmationAsync(CancellationToken cancellationToken)
    {
        var profile = await Sender.Send(new GetMyProfileQuery(), cancellationToken);
        if (profile is not null)
        {
            await Sender.Send(new ResendConfirmationEmailCommand(profile.Email), cancellationToken);
            StatusMessage = "We sent a new confirmation link to your email.";
        }

        return RedirectToPage();
    }

    public sealed class ProfileInput
    {
        [Required, StringLength(120)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Phone]
        [Display(Name = "Mobile phone")]
        public string? PhoneNumber { get; set; }

        [Display(Name = "Street address")]
        public string? AddressLine1 { get; set; }

        [Display(Name = "Apt, suite")]
        public string? AddressLine2 { get; set; }

        public string? City { get; set; }

        public string? State { get; set; }

        [Display(Name = "ZIP code")]
        public string? PostalCode { get; set; }
    }
}
