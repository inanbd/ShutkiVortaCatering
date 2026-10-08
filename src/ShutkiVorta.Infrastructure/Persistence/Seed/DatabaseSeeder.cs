using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Domain.Menu;
using ShutkiVorta.Infrastructure.Identity;

namespace ShutkiVorta.Infrastructure.Persistence.Seed;

internal sealed class DatabaseSeeder(
    RoleManager<ApplicationRole> roles,
    UserManager<ApplicationUser> users,
    IMenuItemRepository menu,
    IDateTimeProvider clock,
    IOptions<SeedOptions> options,
    ILogger<DatabaseSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync();
        await SeedAdministratorAsync();
        if (options.Value.SeedMenu)
        {
            await SeedMenuAsync(cancellationToken);
        }
    }

    private async Task SeedRolesAsync()
    {
        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new ApplicationRole { Name = role });
                logger.LogInformation("Created role {Role}", role);
            }
        }
    }

    private async Task SeedAdministratorAsync()
    {
        var email = options.Value.AdminEmail;
        var password = options.Value.AdminPassword;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("No Seed:AdminEmail/Seed:AdminPassword configured; skipping administrator seeding");
            return;
        }

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = options.Value.AdminName,
                CreatedAtUtc = clock.UtcNow,
            };

            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                logger.LogError("Could not create administrator {Email}: {Errors}", email, string.Join("; ", result.Errors.Select(e => e.Description)));
                return;
            }

            logger.LogInformation("Created administrator account {Email}", email);
        }

        if (!await users.IsInRoleAsync(user, Roles.Admin))
        {
            await users.AddToRoleAsync(user, Roles.Admin);
        }
    }

    private async Task SeedMenuAsync(CancellationToken cancellationToken)
    {
        if (await menu.CountAsync(cancellationToken) > 0)
        {
            return;
        }

        foreach (var details in MenuSeedData.Items)
        {
            await menu.AddAsync(MenuItem.Create(details, clock.UtcNow), cancellationToken);
        }

        logger.LogInformation("Seeded {Count} menu items", MenuSeedData.Items.Count);
    }
}
