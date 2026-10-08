using System.ComponentModel.DataAnnotations;

namespace ShutkiVorta.Web.Pages;

public sealed class InquiryInput
{
    [Required(ErrorMessage = "Please tell us your name.")]
    [StringLength(120)]
    [Display(Name = "Your name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter your email address.")]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Phone]
    [Display(Name = "Phone (optional)")]
    public string? Phone { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Event date")]
    public DateOnly? EventDate { get; set; }

    [Range(1, 5000)]
    [Display(Name = "Number of guests")]
    public int? GuestCount { get; set; }

    /// <summary>Honeypot: hidden from people, but spam bots fill it in.</summary>
    public string? Website { get; set; }

    public bool LooksLikeSpam => !string.IsNullOrWhiteSpace(Website);

    [Required(ErrorMessage = "Please write a short message.")]
    [StringLength(4000)]
    [Display(Name = "Tell us about your event")]
    public string Message { get; set; } = string.Empty;
}
