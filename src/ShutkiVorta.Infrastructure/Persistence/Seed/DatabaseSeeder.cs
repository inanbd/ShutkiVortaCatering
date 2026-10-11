using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Inventory;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Domain.Inventory;
using ShutkiVorta.Domain.Menu;
using ShutkiVorta.Infrastructure.Identity;

namespace ShutkiVorta.Infrastructure.Persistence.Seed;

internal sealed class DatabaseSeeder(
    RoleManager<ApplicationRole> roles,
    UserManager<ApplicationUser> users,
    IMenuItemRepository menu,
    IInventoryRepository inventory,
    IDateTimeProvider clock,
    IOptions<SeedOptions> options,
    ILogger<DatabaseSeeder> logger)
{
    /// <param name="appliedMigrations">The migrations applied during this start, so one-time data is added exactly once.</param>
    public async Task SeedAsync(IReadOnlyCollection<string> appliedMigrations, CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync();
        await SeedAdministratorAsync();
        if (options.Value.SeedMenu)
        {
            await SeedMenuAsync(cancellationToken);
        }

        // Only when the inventory tables were just created: sample entries an admin deleted must not come back on restart.
        if (options.Value.SeedInventory && appliedMigrations.Contains(InventorySeedData.MigrationId, StringComparer.OrdinalIgnoreCase))
        {
            await SeedInventoryAsync(cancellationToken);
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

    /// <summary>
    /// Creates the Seed:AdminEmail account as an Admin. An existing account only becomes Admin again when NO account holds
    /// the Admin role (break-glass recovery): an admin who was deliberately demoted or removed must stay that way.
    /// </summary>
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

            await users.AddToRoleAsync(user, Roles.Admin);
            logger.LogInformation("Created administrator account {Email}", email);
            return;
        }

        if ((await users.GetUsersInRoleAsync(Roles.Admin)).Count > 0)
        {
            return;
        }

        // Nobody can manage the site: restore the configured account as an Admin (and nothing else).
        foreach (var other in Roles.Staff.Where(r => r != Roles.Admin))
        {
            if (await users.IsInRoleAsync(user, other))
            {
                await users.RemoveFromRoleAsync(user, other);
            }
        }

        await users.AddToRoleAsync(user, Roles.Admin);
        logger.LogWarning("No account held the Admin role, so {Email} (Seed:AdminEmail) was made an Admin again", email);
        if (!user.EmailConfirmed)
        {
            // Not confirmed on purpose: whoever registered the address must prove they own it (a password reset does).
            logger.LogWarning("{Email} has not confirmed its email address; use \"Forgot password\" to confirm it and get admin access", email);
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

    private async Task SeedInventoryAsync(CancellationToken cancellationToken)
    {
        if (await inventory.CountItemsAsync(cancellationToken) > 0)
        {
            return;
        }

        var now = clock.UtcNow;
        foreach (var (name, unit) in InventorySeedData.Items)
        {
            await inventory.AddItemAsync(InventoryItem.Create(name, unit, now), cancellationToken);
        }

        var today = DateOnly.FromDateTime(clock.BusinessNow);
        foreach (var sample in InventorySeedData.Purchases)
        {
            var purchase = InventoryPurchase.Record(
                today.AddDays(-sample.DaysAgo), sample.Store, InventorySeedData.SampleNote, sample.Lines, InventorySeedData.SampleCreatedBy, today, now);
            await inventory.AddPurchaseAsync(purchase, cancellationToken);
        }

        logger.LogInformation("Seeded {Items} inventory items and {Purchases} sample purchases", InventorySeedData.Items.Count, InventorySeedData.Purchases.Count);
    }
}
