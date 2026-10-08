using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Account;

public sealed class RegisterModel(ISender sender) : AppPageModel(sender)
{
    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public void OnGet() => ViewData["Title"] = "Create an account";

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Create an account";
        if (!ModelState.IsValid)
        {
            return Page();
        }

        RegisterResult? result = null;
        var ok = await TryExecuteAsync(
            async () => result = await Sender.Send(new RegisterCustomerCommand(Input.FullName, Input.Email, Input.Phone, Input.Password), cancellationToken),
            typeof(RegisterInput));

        if (!ok || result is null)
        {
            return Page();
        }

        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return Page();
        }

        if (result.RequiresEmailConfirmation)
        {
            return Redirect($"/account/register-confirmation?email={Uri.EscapeDataString(Input.Email)}");
        }

        StatusMessage = "স্বাগতম! Your account is ready. We sent a link to confirm your email address.";
        return LocalRedirect(SafeReturnUrl(ReturnUrl) ?? "/account/orders");
    }

    public sealed class RegisterInput
    {
        [Required, StringLength(120)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required, Phone]
        [Display(Name = "Mobile phone")]
        public string Phone { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
