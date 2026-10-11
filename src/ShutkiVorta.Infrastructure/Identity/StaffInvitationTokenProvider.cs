using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Features.Staff;

namespace ShutkiVorta.Infrastructure.Identity;

/// <summary>
/// Staff invitation codes: like password-reset codes (tied to the security stamp, so a new invitation, a changed email or
/// the password being set ends them), but with their own protector and a lifespan of a week instead of a day.
/// </summary>
internal sealed class StaffInvitationTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<StaffInvitationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(dataProtectionProvider, options, logger)
{
    /// <summary>The provider's name in Identity's token provider map.</summary>
    public const string ProviderName = "StaffInvitation";

    /// <summary>The purpose every invitation code is made for (a code for another purpose is refused).</summary>
    public const string Purpose = "StaffInvitation";
}

internal sealed class StaffInvitationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public StaffInvitationTokenProviderOptions()
    {
        Name = StaffInvitationTokenProvider.ProviderName;
        TokenLifespan = StaffInvitations.ValidFor;
    }
}
