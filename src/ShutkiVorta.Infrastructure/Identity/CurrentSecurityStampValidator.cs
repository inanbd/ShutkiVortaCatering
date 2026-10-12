using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ShutkiVorta.Infrastructure.Identity;

/// <summary>
/// Ends a sign-in on its very next request once the account's security stamp changes (a password set, reset or removed, an
/// unconfirmed account taken over as an invitation, an email changed), instead of at Identity's next periodic check up to
/// <see cref="SecurityStampValidatorOptions.ValidationInterval"/> later. Otherwise a session that was meant to end could
/// still use the account in the meantime: read and change the profile, place orders, and (through the cookie a profile save
/// re-issues) even come back with the new stamp. One indexed lookup per signed-in request; the periodic check that
/// refreshes the cookie's claims still runs as before.
/// </summary>
internal sealed class CurrentSecurityStampValidator(
    IOptions<SecurityStampValidatorOptions> options,
    SignInManager<ApplicationUser> signInManager,
    ILoggerFactory logger) : SecurityStampValidator<ApplicationUser>(options, signInManager, logger)
{
    public override async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal?.Identity?.IsAuthenticated == true && await VerifySecurityStamp(context.Principal) is null)
        {
            Logger.LogDebug("The account's security stamp changed since this sign-in; rejecting the cookie.");
            context.RejectPrincipal();
            await SignInManager.SignOutAsync();
            return;
        }

        await base.ValidateAsync(context);
    }
}
