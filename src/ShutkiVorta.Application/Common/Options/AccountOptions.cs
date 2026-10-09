namespace ShutkiVorta.Application.Common.Options;

/// <summary>Customer account rules.</summary>
public sealed class AccountOptions
{
    /// <summary>Kept as "Identity" so existing configuration keys (Identity:RequireConfirmedEmail) still apply.</summary>
    public const string SectionName = "Identity";

    /// <summary>When true, customers must click the link in the confirmation email before they can sign in.</summary>
    public bool RequireConfirmedEmail { get; set; }
}
