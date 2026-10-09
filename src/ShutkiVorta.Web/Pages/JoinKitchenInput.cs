using System.ComponentModel.DataAnnotations;

namespace ShutkiVorta.Web.Pages;

/// <summary>"Join our kitchen" form on /kitchen: a homemaker who would like to cook with us.</summary>
public sealed class JoinKitchenInput
{
    [Required(ErrorMessage = "Please tell us your name.")]
    [StringLength(120)]
    [Display(Name = "Your name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please share a phone number so we can call you.")]
    [Phone(ErrorMessage = "Please enter a valid phone number.")]
    [StringLength(32)]
    [Display(Name = "Phone")]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter your email address.")]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Honeypot: hidden from people, but spam bots fill it in.</summary>
    public string? Website { get; set; }

    public bool LooksLikeSpam => !string.IsNullOrWhiteSpace(Website);

    [Required(ErrorMessage = "Please tell us a little about yourself.")]
    [StringLength(4000)]
    [Display(Name = "Tell us about yourself")]
    public string Message { get; set; } = string.Empty;
}
