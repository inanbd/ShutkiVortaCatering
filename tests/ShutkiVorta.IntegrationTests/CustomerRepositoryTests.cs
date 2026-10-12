using Microsoft.Extensions.DependencyInjection;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Customers;
using ShutkiVorta.Application.Features.Inquiries;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.IntegrationTests.Infrastructure;
using static ShutkiVorta.IntegrationTests.Infrastructure.CustomerTestData;

namespace ShutkiVorta.IntegrationTests;

/// <summary>
/// The customer list's SQL on every provider: which customer each order is counted on, the figures, the types, search,
/// every sort and paging. Each test checks the whole list, so it uses an app (and database) of its own.
/// </summary>
public sealed class CustomerRepositoryTests
{
    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task EveryOrder_IsCountedOnOneCustomer_CancelledOrdersCountForNothing(string provider)
    {
        await using var app = new AppFactory(provider);
        var people = await SeedAsync(app);

        var list = await SearchAsync(app, new CustomerSearch { PageSize = 100 });
        var rows = list.Customers.Items;

        // The seeded Admin and the staff-only Employee are not customers; everyone else is listed once.
        Assert.Equal(8, rows.Count);
        Assert.Equal(8, list.Customers.TotalCount);
        Assert.Equal((4, 3, 1), (list.AccountCount, list.GuestCount, list.RestaurantCount));
        Assert.DoesNotContain(rows, r => r.AccountId == people.Employee);
        Assert.DoesNotContain(rows, r => r.Email == AppFactory.AdminEmail);

        // Ann: her own orders and the guest order placed with her confirmed email, counted once, on her account; the
        // cancelled order is left out of the figures and of the last order.
        var ann = Assert.Single(rows, r => r.AccountId == people.Ann);
        Assert.Equal((CustomerType.Account, 2, 130.50m, (DateTime?)T0.AddDays(3)), (ann.Type, ann.OrderCount, ann.OrderValue, ann.LastOrderUtc));
        Assert.Equal("Ann.Lee@Example.test", ann.Email);
        Assert.Equal(T0.AddDays(-30), ann.JoinedUtc);
        Assert.DoesNotContain(rows, r => r.Type == CustomerType.Guest && r.Email == "ann.lee@example.test");

        // Bob never confirmed his email, so his guest-email order stays a guest row of its own.
        var bob = Assert.Single(rows, r => r.AccountId == people.Bob);
        Assert.Equal((0, 0m, (DateTime?)null), (bob.OrderCount, bob.OrderValue, bob.LastOrderUtc));
        var bobGuest = Assert.Single(rows, r => r.Type == CustomerType.Guest && r.Email == "bob@example.test");
        Assert.Equal(("Bobby Guest", 1, 20m), (bobGuest.Name, bobGuest.OrderCount, bobGuest.OrderValue));
        Assert.Null(bobGuest.AccountId);

        // A guest is one row per email, with the name and phone of the newest order and its number as the page key.
        var carol = Assert.Single(rows, r => r.Email == "carol@example.test");
        Assert.Equal(("Carol New", "214-555-0002", 2, 25m), (carol.Name, carol.Phone, carol.OrderCount, carol.OrderValue));
        Assert.Equal(((DateTime?)T0.AddDays(4), T0.AddDays(1)), (carol.LastOrderUtc, carol.JoinedUtc));
        Assert.Equal(people.CarolNewOrder, carol.GuestOrderNumber);

        // A restaurant: an account with a standing order (and the restaurant's name). Its delivery counts from the day it was
        // generated, although it is not due yet, like any order placed for a later date.
        var dina = Assert.Single(rows, r => r.AccountId == people.Dina);
        Assert.Equal((CustomerType.Restaurant, "Dina's Kitchen", 1, 200m), (dina.Type, dina.BusinessName, dina.OrderCount, dina.OrderValue));
        Assert.Equal(T0.AddDays(6), dina.LastOrderUtc);

        // Staff who order are customers too; only cancelled orders still make someone a customer, with nothing counted.
        Assert.Equal(1, Assert.Single(rows, r => r.AccountId == people.Frank).OrderCount);
        var gus = Assert.Single(rows, r => r.Email == "gus@example.test");
        Assert.Equal((0, 0m, (DateTime?)null, T0.AddDays(7)), (gus.OrderCount, gus.OrderValue, gus.LastOrderUtc, gus.JoinedUtc));
        Assert.Equal(0, Assert.Single(rows, r => r.AccountId == people.Hana).OrderCount);

        // Every non-cancelled order is counted exactly once over the whole list.
        Assert.Equal(7, rows.Sum(r => r.OrderCount));
        Assert.Equal(100m + 30.50m + 20m + 10m + 15m + 200m + 5m, rows.Sum(r => r.OrderValue));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task SearchAndTypes_PickRows_WithoutChangingTheirFigures(string provider)
    {
        await using var app = new AppFactory(provider);
        var people = await SeedAsync(app);

        // "ann" matches Ann by name and email; her figures still include every order (the search is on the outer query).
        var ann = await SearchAsync(app, new CustomerSearch { Search = "ann" });
        var row = Assert.Single(ann.Customers.Items);
        Assert.Equal((people.Ann, 2, 130.50m), (row.AccountId, row.OrderCount, row.OrderValue));
        Assert.Equal((1, 0, 0, 1), (ann.AccountCount, ann.GuestCount, ann.RestaurantCount, ann.Customers.TotalCount));

        // A guest is found by the phone of their newest order, not by an older name; a restaurant by its name.
        Assert.Equal("carol@example.test", Assert.Single((await SearchAsync(app, new CustomerSearch { Search = "555-0002" })).Customers.Items).Email);
        Assert.Empty((await SearchAsync(app, new CustomerSearch { Search = "Carol Old" })).Customers.Items);
        Assert.Equal(people.Dina, Assert.Single((await SearchAsync(app, new CustomerSearch { Search = "Dina's" })).Customers.Items).AccountId);

        // Types: accounts leave restaurants out; the counts of every type stay for the tabs.
        var accounts = await SearchAsync(app, new CustomerSearch { Type = CustomerType.Account, PageSize = 100 });
        Assert.Equal(new[] { people.Ann, people.Bob, people.Frank, people.Hana }.Order(), accounts.Customers.Items.Select(r => r.AccountId!).Order());
        Assert.Equal((4, 4, 3, 1), (accounts.Customers.TotalCount, accounts.AccountCount, accounts.GuestCount, accounts.RestaurantCount));

        var guests = await SearchAsync(app, new CustomerSearch { Type = CustomerType.Guest, PageSize = 100 });
        Assert.Equal(["bob@example.test", "carol@example.test", "gus@example.test"], guests.Customers.Items.Select(r => r.Email).Order());
        Assert.All(guests.Customers.Items, r => Assert.Null(r.AccountId));

        var restaurants = await SearchAsync(app, new CustomerSearch { Type = CustomerType.Restaurant });
        Assert.Equal(people.Dina, Assert.Single(restaurants.Customers.Items).AccountId);
        Assert.Equal(1, restaurants.Customers.TotalCount);

        // A search within a type: the total is for that type only.
        var bobs = await SearchAsync(app, new CustomerSearch { Search = "bob", Type = CustomerType.Guest });
        Assert.Equal(("bob@example.test", 1), (Assert.Single(bobs.Customers.Items).Email, bobs.Customers.TotalCount));
        Assert.Equal((1, 1), (bobs.AccountCount, bobs.GuestCount));

        // LIKE's wildcards in a search are plain characters: an email with an underscore is found by its exact text (and not
        // by any character in its place), and % and SQL Server's [ ] match only themselves.
        var ian = await CreateAccountAsync(app, "Ian Under", "ian_under@example.test");
        await AddOrderAsync(app, new TestOrder("ian_under@example.test", 8m, T0.AddDays(2)) { CustomerId = ian });
        await AddOrderAsync(app, new TestOrder("ianxunder@example.test", 9m, T0.AddDays(2)) { Name = "Ian [VIP]" });
        await AddOrderAsync(app, new TestOrder("percent@example.test", 7m, T0.AddDays(2)) { Name = "100% Halal" });
        foreach (var term in new[] { "ian_under", "ian_under@example.test" })
        {
            Assert.Equal(ian, Assert.Single((await SearchAsync(app, new CustomerSearch { Search = term })).Customers.Items).AccountId);
        }

        Assert.Equal("ianxunder@example.test", Assert.Single((await SearchAsync(app, new CustomerSearch { Search = "[VIP]" })).Customers.Items).Email);
        Assert.Empty((await SearchAsync(app, new CustomerSearch { Search = "[A-Z]" })).Customers.Items);
        Assert.Equal("percent@example.test", Assert.Single((await SearchAsync(app, new CustomerSearch { Search = "100%" })).Customers.Items).Email);
        Assert.Equal("percent@example.test", Assert.Single((await SearchAsync(app, new CustomerSearch { Search = "%" })).Customers.Items).Email);
        Assert.Equal(ian, Assert.Single((await SearchAsync(app, new CustomerSearch { Search = "_" })).Customers.Items).AccountId);
        Assert.Empty((await SearchAsync(app, new CustomerSearch { Search = @"\" })).Customers.Items);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task EverySort_HasItsOrder_AndPagesNeverRepeatOrSkipRows(string provider)
    {
        await using var app = new AppFactory(provider);
        var people = await SeedAsync(app);

        // Rows that tie on everything a sort looks at: guests with the same name, value and order time, and accounts without
        // orders that joined at the same moment.
        for (var i = 0; i < 6; i++)
        {
            await AddOrderAsync(app, new TestOrder($"twin{i}@example.test", 12m, T0.AddDays(2)) { Name = "Twin" });
            await CreateAccountAsync(app, "Twin", $"twin-account{i}@example.test", createdAtUtc: T0.AddDays(-1));
        }

        string Key(CustomerListItemDto r) => r.AccountId ?? r.Email;

        var byLastOrder = await AllAsync(app, CustomerSort.LastOrder);
        Assert.Equal(
            [people.Frank, people.Dina, "carol@example.test", people.Ann],
            byLastOrder.Take(4).Select(Key));
        Assert.Equal(["gus@example.test"], byLastOrder.SkipWhile(r => r.LastOrderUtc is not null).Take(1).Select(Key)); // guests first among ties
        Assert.True(byLastOrder.SkipWhile(r => r.LastOrderUtc is not null).All(r => r.LastOrderUtc is null)); // no last order: at the end

        var byValue = await AllAsync(app, CustomerSort.OrderValue);
        Assert.Equal([people.Dina, people.Ann, "carol@example.test", "bob@example.test"], byValue.Take(4).Select(Key));
        Assert.Equal(byValue.Select(r => r.OrderValue).OrderDescending(), byValue.Select(r => r.OrderValue));

        var byName = (await AllAsync(app, CustomerSort.Name)).Where(r => r.Name != "Twin").Select(r => r.Name);
        Assert.Equal(["Ann Lee", "Bob Unconfirmed", "Bobby Guest", "Carol New", "Dina Restaurant", "Frank Staff", "Gus Cancelled", "hana lower"], byName);

        var newest = (await AllAsync(app, CustomerSort.Newest)).Where(r => r.Name != "Twin").Select(Key);
        Assert.Equal([people.Hana, "gus@example.test", "bob@example.test", "carol@example.test", people.Frank, people.Dina, people.Bob, people.Ann], newest);

        // Page by page (3 at a time) gives exactly the one-page list, for every sort and type: nothing repeated or skipped.
        foreach (var sort in Enum.GetValues<CustomerSort>())
        {
            foreach (var type in new CustomerType?[] { null, CustomerType.Account, CustomerType.Guest })
            {
                var whole = await SearchAsync(app, new CustomerSearch { Sort = sort, Type = type, PageSize = 100 });
                var paged = new List<CustomerListItemDto>();
                for (var page = 1; page <= (whole.Customers.TotalCount + 2) / 3; page++)
                {
                    var part = await SearchAsync(app, new CustomerSearch { Sort = sort, Type = type, Page = page, PageSize = 3 });
                    Assert.Equal(whole.Customers.TotalCount, part.Customers.TotalCount);
                    paged.AddRange(part.Customers.Items);
                }

                Assert.Equal(whole.Customers.Items.Select(Key), paged.Select(Key));
                Assert.Equal(whole.Customers.TotalCount, paged.Select(Key).Distinct().Count());
            }
        }

        Assert.Equal(20, (await SearchAsync(app, new CustomerSearch())).Customers.TotalCount);

        // A page past the end (an old link, or a made-up page number) is the last page, whatever the number.
        var lastPage = await SearchAsync(app, new CustomerSearch { Page = 7, PageSize = 3 });
        Assert.Equal((7, 7, 2), (lastPage.Customers.Page, lastPage.Customers.TotalPages, lastPage.Customers.Items.Count));
        foreach (var page in new[] { 8, 100_000_000, int.MaxValue })
        {
            var past = await SearchAsync(app, new CustomerSearch { Page = page, PageSize = 3 });
            Assert.Equal((7, 20), (past.Customers.Page, past.Customers.TotalCount));
            Assert.Equal(lastPage.Customers.Items.Select(Key), past.Customers.Items.Select(Key));
        }

        var nobody = await SearchAsync(app, new CustomerSearch { Search = "nobody at all", Page = 5 });
        Assert.Equal((1, 0, 0), (nobody.Customers.Page, nobody.Customers.TotalPages, nobody.Customers.Items.Count));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task CustomerPages_UseTheSameRules_AsTheList(string provider)
    {
        await using var app = new AppFactory(provider);
        var people = await SeedAsync(app);
        using var scope = app.Services.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();

        // Ann: the same figures as her row, her saved address and the newest delivery address; her orders include the guest
        // one and the cancelled one (newest first).
        var ann = await customers.GetAccountAsync(people.Ann);
        Assert.NotNull(ann);
        Assert.Equal((CustomerType.Account, 3, 2, 1, 130.50m, 65.25m), (ann.Type, ann.AllOrderCount, ann.OrderCount, ann.CancelledCount, ann.OrderValue, ann.AverageOrder));
        Assert.Equal(((DateTime?)T0.AddDays(1), (DateTime?)T0.AddDays(3)), (ann.FirstOrderUtc, ann.LastOrderUtc));
        Assert.Equal(("1 Saved St, Dallas, TX 75201", "9 Delivery Rd, Dallas, TX 75202"), (ann.SavedAddress, ann.LatestDeliveryAddress));
        Assert.True(ann.EmailConfirmed);
        Assert.False(ann.IsStaff);
        var annOrders = await customers.GetAccountOrdersAsync(people.Ann, 50);
        Assert.Equal([OrderStatus.Cancelled, OrderStatus.Pending, OrderStatus.Completed], annOrders.Select(o => o.Status));
        Assert.Equal(2, (await customers.GetAccountOrdersAsync(people.Ann, 2)).Count);

        // Staff: a customer with orders is flagged as staff; a staff-only account and an unknown id are not customers.
        Assert.True((await customers.GetAccountAsync(people.Frank))!.IsStaff);
        Assert.Null(await customers.GetAccountAsync(people.Employee));
        Assert.Null(await customers.GetAccountAsync(Guid.NewGuid().ToString()));
        Assert.Equal(CustomerType.Restaurant, (await customers.GetAccountAsync(people.Dina))!.Type);
        Assert.Equal((false, 0), ((await customers.GetAccountAsync(people.Bob))!.EmailConfirmed, (await customers.GetAccountAsync(people.Bob))!.AllOrderCount));

        // Who an order is counted on: the account that placed it, the account with that confirmed email, else the guest.
        Assert.Equal(people.Ann, (await customers.GetOrderOwnerAsync(people.AnnOwnOrder))!.AccountId);
        Assert.Equal(people.Ann, (await customers.GetOrderOwnerAsync(people.AnnGuestOrder))!.AccountId);
        var bobOrder = await customers.GetOrderOwnerAsync(people.BobGuestOrder);
        Assert.Equal(("bob@example.test", (string?)null), (bobOrder!.Email, bobOrder.AccountId));
        Assert.Null(await customers.GetOrderOwnerAsync("SV-NO-SUCH-ORDER"));

        // A guest: their figures, newest name and phone, and the unconfirmed account with their email (a customer itself).
        var carol = await customers.GetGuestAsync("carol@example.test");
        Assert.Equal(("Carol New", "214-555-0002", 2, 25m, 12.50m), (carol!.Name, carol.Phone, carol.OrderCount, carol.OrderValue, carol.AverageOrder));
        Assert.Null(carol.UnconfirmedAccountId);
        Assert.Equal(2, (await customers.GetGuestOrdersAsync("carol@example.test", 50)).Count);
        Assert.Equal(people.Bob, (await customers.GetGuestAsync("bob@example.test"))!.UnconfirmedAccountId);
        Assert.Null(await customers.GetGuestAsync("ann.lee@example.test")); // counted on Ann's account
        Assert.Null(await customers.GetGuestAsync("nobody@example.test"));
        Assert.Empty(await customers.GetGuestOrdersAsync("ann.lee@example.test", 50));

        // Inquiries are found by the same email, whatever its casing.
        await AddInquiryAsync(app, "Ann", "Ann.Lee@Example.test", "A wedding in May", T0);
        await AddInquiryAsync(app, "Ann", "ann.lee@example.test", "And a birthday", T0.AddDays(1));
        var inquiries = await scope.ServiceProvider.GetRequiredService<ICateringInquiryRepository>().GetByEmailAsync(ann.Email, 10);
        Assert.Equal(["And a birthday", "A wedding in May"], inquiries.Select(i => i.Message));
    }

    private sealed record People(
        string Ann, string Bob, string Dina, string Frank, string Hana, string Employee,
        string AnnOwnOrder, string AnnGuestOrder, string BobGuestOrder, string CarolNewOrder);

    /// <summary>
    /// Ann (confirmed; own orders, a cancelled one and a guest order with her email), Bob (unconfirmed, with a guest-email
    /// order), Carol (a guest, twice), Dina (a restaurant), Frank (staff who orders), Gus (a guest who cancelled), Hana
    /// (registered, no orders) and an Employee who is only staff.
    /// </summary>
    private static async Task<People> SeedAsync(AppFactory app)
    {
        var ann = await CreateAccountAsync(app, "Ann Lee", "Ann.Lee@Example.test", createdAtUtc: T0.AddDays(-30), phone: "214-555-0101", street: "1 Saved St");
        var annOwn = await AddOrderAsync(app, new TestOrder("ann.lee@example.test", 100m, T0.AddDays(1)) { CustomerId = ann, Name = "Ann Lee", Status = OrderStatus.Completed });
        await AddOrderAsync(app, new TestOrder("ann.lee@example.test", 50m, T0.AddDays(5)) { CustomerId = ann, Name = "Ann Lee", Status = OrderStatus.Cancelled });
        var annGuest = await AddOrderAsync(app, new TestOrder("ANN.LEE@example.test", 30.50m, T0.AddDays(3)) { Name = "Ann L", DeliveryStreet = "9 Delivery Rd" });

        var bob = await CreateAccountAsync(app, "Bob Unconfirmed", "bob@example.test", confirmed: false, createdAtUtc: T0.AddDays(-20));
        var bobGuest = await AddOrderAsync(app, new TestOrder("bob@example.test", 20m, T0.AddDays(2)) { Name = "Bobby Guest" });

        await AddOrderAsync(app, new TestOrder("carol@example.test", 10m, T0.AddDays(1)) { Name = "Carol Old", Phone = "214-555-0001" });
        var carolNew = await AddOrderAsync(app, new TestOrder("carol@example.test", 15m, T0.AddDays(4)) { Name = "Carol New", Phone = "214-555-0002" });

        var dina = await CreateAccountAsync(app, "Dina Restaurant", "dina@example.test", createdAtUtc: T0.AddDays(-10));
        var standing = await AddStandingOrderAsync(app, dina, "Dina's Kitchen", "dina@example.test");
        await AddOrderAsync(app, new TestOrder("dina@example.test", 200m, T0.AddDays(6))
        {
            CustomerId = dina, Name = "Dina Restaurant", Status = OrderStatus.Confirmed, StandingOrderId = standing, ScheduledFor = DateTime.UtcNow.AddDays(7),
        });

        var frank = await CreateAccountAsync(app, "Frank Staff", "frank@example.test", customerRole: false, createdAtUtc: T0.AddDays(-5));
        using (var scope = app.Services.CreateScope())
        {
            var assigned = await scope.ServiceProvider.GetRequiredService<IStaffPermissionStore>().SetStaffRoleAsync(frank, StaffRole.Manager, "test-setup", "tests");
            Assert.True(assigned.Succeeded);
        }

        await AddOrderAsync(app, new TestOrder("frank@example.test", 5m, T0.AddDays(8)) { CustomerId = frank, Name = "Frank Staff" });

        await AddOrderAsync(app, new TestOrder("gus@example.test", 40m, T0.AddDays(7)) { Name = "Gus Cancelled", Status = OrderStatus.Cancelled });
        var hana = await CreateAccountAsync(app, "hana lower", "hana@example.test", createdAtUtc: T0.AddDays(10));
        var employee = await StaffTestHelper.CreateStaffAsync(app, StaffRole.Employee);

        return new People(ann, bob, dina, frank, hana, employee.UserId, annOwn, annGuest, bobGuest, carolNew);
    }

    private static async Task<CustomerListDto> SearchAsync(AppFactory app, CustomerSearch search)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICustomerRepository>().SearchAsync(search);
    }

    private static async Task<IReadOnlyList<CustomerListItemDto>> AllAsync(AppFactory app, CustomerSort sort) =>
        (await SearchAsync(app, new CustomerSearch { Sort = sort, PageSize = 100 })).Customers.Items;
}
