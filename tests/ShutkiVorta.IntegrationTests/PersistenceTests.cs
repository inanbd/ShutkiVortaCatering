using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Inquiries;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Application.Features.Settings;
using ShutkiVorta.Domain.Inquiries;
using ShutkiVorta.Domain.Menu;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.Infrastructure.Identity;
using ShutkiVorta.IntegrationTests.Infrastructure;

namespace ShutkiVorta.IntegrationTests;

/// <summary>Runs the Dapper repositories and Identity stores against every configured database provider.</summary>
[Collection("app")]
public sealed class PersistenceTests(TestServers servers)
{
    private static readonly DateTime Now = new(2026, 10, 8, 15, 0, 0, DateTimeKind.Utc);

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task Database_IsMigratedAndSeeded(string provider)
    {
        using var scope = servers.Get(provider).Services.CreateScope();
        var info = scope.ServiceProvider.GetRequiredService<IDatabaseInfo>();
        var menu = scope.ServiceProvider.GetRequiredService<IMenuItemRepository>();

        Assert.Contains(provider == "Sqlite" ? "SQLite" : "SQL Server", info.ProviderName);
        var items = await menu.GetAllAsync(includeUnavailable: true);
        Assert.True(items.Count >= 9);
        Assert.Contains(items, i => i.Slug == "loitta-shutki-vorta" && i.Category == MenuCategory.ShutkiVorta && i.PricePerUnit == 24.99m);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task MenuItems_RoundTrip(string provider)
    {
        using var scope = servers.Get(provider).Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMenuItemRepository>();
        var slug = $"test-vorta-{Guid.NewGuid():N}"[..20];

        var item = MenuItem.Create(new MenuItemDetails
        {
            Name = "Test Vorta",
            BengaliName = "পরীক্ষা ভর্তা",
            Slug = slug,
            ShortDescription = "Short",
            Description = "Long",
            PricePerUnit = 12.75m,
            MinimumQuantity = 0.5m,
            QuantityStep = 0.25m,
            IsAvailable = false,
        }, Now);
        await repository.AddAsync(item);
        Assert.True(item.Id > 0);

        var loaded = await repository.GetBySlugAsync(slug);
        Assert.NotNull(loaded);
        Assert.Equal("পরীক্ষা ভর্তা", loaded.BengaliName);
        Assert.Equal(12.75m, loaded.PricePerUnit);
        Assert.Equal(0.25m, loaded.QuantityStep);
        Assert.False(loaded.IsAvailable);
        Assert.Equal(Now, DateTime.SpecifyKind(loaded.CreatedAtUtc, DateTimeKind.Utc));

        Assert.True(await repository.SlugExistsAsync(slug, excludeId: null));
        Assert.False(await repository.SlugExistsAsync(slug, excludeId: item.Id));

        loaded.SetAvailability(true, Now.AddHours(1));
        await repository.UpdateAsync(loaded);
        Assert.True((await repository.GetByIdAsync(item.Id))!.IsAvailable);

        await repository.DeleteAsync(item.Id);
        Assert.Null(await repository.GetByIdAsync(item.Id));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task Orders_RoundTrip_WithWholeAndFractionalPounds(string provider)
    {
        using var scope = servers.Get(provider).Services.CreateScope();
        var menu = scope.ServiceProvider.GetRequiredService<IMenuItemRepository>();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var items = await menu.GetAllAsync(includeUnavailable: false);
        var number = $"SV-T-{Guid.NewGuid():N}"[..14].ToUpperInvariant();

        // Whole (2 lb) and fractional (1.5 lb) quantities: SQLite stores these as INTEGER and REAL in the same column.
        var order = Order.Place(
            number, "token-" + number, null,
            new CustomerContact("Test Customer", "Customer@Test.Local", "214-555-0100"),
            FulfillmentMethod.Delivery,
            new DeliveryAddress("1 Elm St", "Apt 2", "Dallas", "TX", "75201"),
            new DateTime(2026, 10, 12, 13, 0, 0),
            "Ring the bell",
            [new OrderLineRequest(items[0], 2m), new OrderLineRequest(items[1], 1.5m)],
            new OrderPricingPolicy(10m, 150m, 0.0825m, 40m),
            Now);
        await orders.AddAsync(order);

        var loaded = await orders.GetByNumberAsync(number);
        Assert.NotNull(loaded);
        Assert.Equal(order.Total, loaded.Total);
        Assert.Equal(order.Tax, loaded.Tax);
        Assert.Equal([2m, 1.5m], loaded.Lines.Select(l => l.Quantity).ToArray());
        Assert.Equal(FulfillmentMethod.Delivery, loaded.Fulfillment);
        Assert.Equal("1 Elm St, Apt 2, Dallas, TX 75201", loaded.DeliveryAddress!.ToString());
        Assert.Equal(new DateTime(2026, 10, 12, 13, 0, 0), loaded.ScheduledFor);
        Assert.Single(loaded.History);

        loaded.ChangeStatus(OrderStatus.Confirmed, "Confirmed by test", "admin", Now.AddMinutes(1));
        loaded.UpdateAdminNotes("Use the blue container", Now.AddMinutes(1));
        await orders.UpdateAsync(loaded);

        var reloaded = await orders.GetByNumberAsync(number);
        Assert.Equal(OrderStatus.Confirmed, reloaded!.Status);
        Assert.Equal("Use the blue container", reloaded.AdminNotes);
        Assert.Equal(2, reloaded.History.Count);

        var search = await orders.SearchAsync(new OrderSearchCriteria { Search = number, Status = OrderStatus.Confirmed, Page = 1, PageSize = 10 });
        var summary = Assert.Single(search.Items);
        Assert.Contains("2 lb", summary.ItemsPreview);
        Assert.Contains("1.5 lb", summary.ItemsPreview);

        var counts = await orders.CountByStatusAsync();
        Assert.True(counts[OrderStatus.Confirmed] >= 1);

        var popular = await orders.GetPopularItemsSinceAsync(Now.AddDays(-1), 5);
        Assert.Contains(popular, p => p.ItemName == items[0].Name && p.TotalQuantity >= 2m);

        var scheduled = await orders.GetScheduledBetweenAsync(new DateTime(2026, 10, 12), new DateTime(2026, 10, 12, 23, 59, 0));
        Assert.Contains(scheduled, o => o.OrderNumber == number);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task Inquiries_RoundTrip(string provider)
    {
        using var scope = servers.Get(provider).Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICateringInquiryRepository>();

        var inquiry = CateringInquiry.Submit("Farhan", "farhan@test.local", null, new DateTime(2026, 12, 1), 150, "Wedding!", Now);
        await repository.AddAsync(inquiry);
        var open = await repository.CountOpenAsync();

        inquiry.MarkHandled(true, Now);
        await repository.UpdateAsync(inquiry);

        var loaded = await repository.GetByIdAsync(inquiry.Id);
        Assert.True(loaded!.IsHandled);
        Assert.Equal(150, loaded.GuestCount);
        Assert.Equal(open - 1, await repository.CountOpenAsync());
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task IdentityStores_WorkWithoutEntityFramework(string provider)
    {
        using var scope = servers.Get(provider).Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"user-{Guid.NewGuid():N}@test.local";

        var user = new ApplicationUser { UserName = email, Email = email, FullName = "Test User" };
        Assert.True((await users.CreateAsync(user, "password123")).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, Roles.Customer)).Succeeded);

        var found = await users.FindByEmailAsync(email.ToUpperInvariant());
        Assert.NotNull(found);
        Assert.True(await users.CheckPasswordAsync(found, "password123"));
        Assert.True(await users.IsInRoleAsync(found, Roles.Customer));
        Assert.False(await users.IsInRoleAsync(found, Roles.Admin));

        found.FullName = "Renamed User";
        Assert.True((await users.UpdateAsync(found)).Succeeded);
        Assert.Equal("Renamed User", (await users.FindByIdAsync(found.Id))!.FullName);

        // A stale copy must be rejected (optimistic concurrency).
        user.FullName = "Stale";
        Assert.False((await users.UpdateAsync(user)).Succeeded);

        await users.AccessFailedAsync(found);
        Assert.Equal(1, await users.GetAccessFailedCountAsync(found));

        var admin = await users.FindByEmailAsync(AppFactory.AdminEmail);
        Assert.NotNull(admin);
        Assert.True(await users.IsInRoleAsync(admin, Roles.Admin));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task PlacingAnOrder_SendsKitchenAndCustomerEmails(string provider)
    {
        var factory = servers.Get(provider);
        using var scope = factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var checkout = await sender.Send(new GetCheckoutOptionsQuery());
        var day = checkout.Days[0];
        var menu = await sender.Send(new GetMenuQuery());

        var result = await sender.Send(new PlaceOrderCommand
        {
            CustomerName = "Email Tester",
            Email = "email.tester@test.local",
            Phone = "214-555-0123",
            Fulfillment = FulfillmentMethod.Pickup,
            ScheduledDate = day.Date,
            ScheduledTime = day.Slots[0],
            Lines = [new CartLineInput(menu[0].Id, 1m)],
        });

        var files = await WaitForMailAsync(factory.MailDirectory, result.OrderNumber, expected: 2);
        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => File.ReadAllText(f).Contains($"https://shutki.test/order/{result.OrderNumber}?token="));
        Assert.Contains(files, f => File.ReadAllText(f).Contains($"https://shutki.test/admin/orders/{result.OrderNumber}"));
        foreach (var file in files)
        {
            var html = await File.ReadAllTextAsync(file);
            Assert.Contains(result.OrderNumber, html);
            Assert.DoesNotContain("{{", html);
            Assert.Contains("https://shutki.test/", html);
        }
    }

    private static async Task<List<string>> WaitForMailAsync(string directory, string orderNumber, int expected)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (Directory.Exists(directory))
            {
                var matches = Directory.GetFiles(directory, "*.html")
                    .Where(f => File.ReadAllText(f).Contains(orderNumber))
                    .ToList();
                if (matches.Count >= expected)
                {
                    return matches;
                }
            }

            await Task.Delay(100);
        }

        return [];
    }
}
