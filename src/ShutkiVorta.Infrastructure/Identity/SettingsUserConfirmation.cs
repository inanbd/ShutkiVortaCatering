using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.Infrastructure.Identity;

/// <summary>
/// Applies Admin → Settings → Customer accounts → "Require email confirmation" at sign-in time, so changing it needs no restart.
/// (Identity is configured with RequireConfirmedAccount, which defers to this check.) Administrators are never locked out by it,
/// so turning the setting on cannot block the people who would have to turn it off again.
/// </summary>
internal sealed class SettingsUserConfirmation(IOptionsMonitor<AccountOptions> account) : IUserConfirmation<ApplicationUser>
{
    public async Task<bool> IsConfirmedAsync(UserManager<ApplicationUser> manager, ApplicationUser user) =>
        !account.CurrentValue.RequireConfirmedEmail
        || await manager.IsEmailConfirmedAsync(user)
        || await manager.IsInRoleAsync(user, Roles.Admin);
}
