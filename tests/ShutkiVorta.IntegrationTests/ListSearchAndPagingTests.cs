using System.Net;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Emails;
using ShutkiVorta.Application.Features.Inquiries;
using ShutkiVorta.Application.Features.Inventory;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Application.Features.TimeClock;
using ShutkiVorta.Application.Features.Wholesale;
using ShutkiVorta.Domain.Inquiries;
using ShutkiVorta.Domain.Inventory;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.Domain.Wholesale;
using ShutkiVorta.IntegrationTests.Infrastructure;
using static ShutkiVorta.IntegrationTests.Infrastructure.StaffTestHelper;

namespace ShutkiVorta.IntegrationTests;

/// <summary>
/// The admin lists' searches and paging on both providers: wildcards typed in a search are literal (emails often have an
/// underscore), and a page number past the end (an old link, a made-up number) shows the last page instead of failing.
/// Each test runs its own app, so the counts are exact.
/// </summary>
public sealed class ListSearchAndPagingTests
{
    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task Searches_TakeUnderscoresAndPercentSignsLiterally(string provider)
    {
        await using var app = new AppFactory(provider);
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        // Orders: the email ann_lee@… is found by what it says; "annxlee" is not an "ann_lee", and "%" is not "everything".
        var annLee = await PlaceOrderAsync(app, "Guest Ann_Lee", "ann_lee@test.local");
        await PlaceOrderAsync(app, "Guest Annxlee", "annxlee@test.local");
        var orders = services.GetRequiredService<IOrderRepository>();
        Assert.Equal([annLee], (await orders.SearchAsync(new OrderSearchCriteria { Search = "ann_lee" })).Items.Select(o => o.OrderNumber));
        Assert.Equal([annLee], (await orders.SearchAsync(new OrderSearchCriteria { Search = " ann_lee@test.local " })).Items.Select(o => o.OrderNumber));
        Assert.Equal([annLee], (await orders.SearchAsync(new OrderSearchCriteria { Search = "_" })).Items.Select(o => o.OrderNumber));
        Assert.Empty((await orders.SearchAsync(new OrderSearchCriteria { Search = "%" })).Items);
        Assert.Empty((await orders.SearchAsync(new OrderSearchCriteria { Search = "[a]" })).Items);
        Assert.Empty((await orders.SearchAsync(new OrderSearchCriteria { Search = @"\" })).Items);

        // Restaurants' standing orders.
        var standingOrders = services.GetRequiredService<IStandingOrderRepository>();
        var chefOne = await AddStandingOrderAsync(services, "chef_one@bistro.test");
        await AddStandingOrderAsync(services, "chefxone@bistro.test");
        Assert.Equal([chefOne], (await standingOrders.SearchAsync(null, "chef_one", 1, 20)).Items.Select(s => s.Id));
        Assert.Empty((await standingOrders.SearchAsync(null, "%", 1, 20)).Items);

        // Inventory purchases (store, notes and item names).
        var inventory = services.GetRequiredService<IInventoryRepository>();
        var cashAndCarry = await AddPurchaseAsync(services, "Cash_and_Carry", "Rice_bag");
        await AddPurchaseAsync(services, "CashXandXCarry", "RiceXbag");
        Assert.Equal([cashAndCarry], (await inventory.SearchPurchasesAsync(new InventoryPurchaseSearch { Search = "h_a" })).Purchases.Items.Select(p => p.Id));
        Assert.Equal([cashAndCarry], (await inventory.SearchPurchasesAsync(new InventoryPurchaseSearch { Search = "rice_b" })).Purchases.Items.Select(p => p.Id));
        Assert.Empty((await inventory.SearchPurchasesAsync(new InventoryPurchaseSearch { Search = "%" })).Purchases.Items);

        // The staff list, on its page too.
        var bossTwo = await CreateStaffAsync(app, StaffRole.Admin, email: "boss_two@test.local");
        await CreateStaffAsync(app, StaffRole.Manager, email: "bossxtwo@test.local");
        var directory = services.GetRequiredService<IStaffDirectory>();
        Assert.Equal([bossTwo.UserId], (await directory.ListAsync(new StaffListFilter(Search: "boss_two"))).Select(m => m.UserId));
        Assert.Equal([bossTwo.UserId], (await directory.ListAsync(new StaffListFilter(Search: "boss_two@test.local"))).Select(m => m.UserId));
        Assert.Empty(await directory.ListAsync(new StaffListFilter(Search: "%")));

        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var users = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/users?q=boss_two"));
        Assert.Contains("boss_two@test.local", users);
        Assert.DoesNotContain("bossxtwo@test.local", users);
        Assert.Contains("Nobody matches", WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/users?q=%25")));
        var orderList = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/orders?q=ann_lee"));
        Assert.Contains(annLee, orderList);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task PagesPastTheEnd_ShowTheLastPage_InEveryList(string provider)
    {
        await using var app = new AppFactory(provider);
        var employee = await CreateStaffAsync(app, StaffRole.Employee);
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var orderNumbers = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            orderNumbers.Add(await PlaceOrderAsync(app, $"Pager {i}", $"pager{i}@test.local"));
        }

        var standingOrder = await AddStandingOrderAsync(services, "pager@bistro.test");
        await services.GetRequiredService<ICateringInquiryRepository>().AddAsync(
            CateringInquiry.Submit("Pager", "pager@test.local", null, null, 20, "A party", DateTime.UtcNow));
        await AddPurchaseAsync(services, "Pager Mart", "Pager item");
        var shifts = services.GetRequiredService<ITimeClockRepository>();
        Assert.True(await shifts.AddClosedShiftAsync(ShutkiVorta.Domain.TimeClock.TimeClockShift.AddByHand(
            employee.UserId, employee.FullName, DateTime.UtcNow.AddDays(-1).AddHours(-8), DateTime.UtcNow.AddDays(-1), "tests", "paging", DateTime.UtcNow)) == ShiftChangeOutcome.Saved);

        foreach (var page in new[] { 2, 100_000_000, 120_000_001, int.MaxValue })
        {
            var orders = await services.GetRequiredService<IOrderRepository>().SearchAsync(new OrderSearchCriteria { Page = page, PageSize = 2 });
            Assert.Equal((2, 3, 1), (orders.Page, orders.TotalCount, orders.Items.Count));
            Assert.Equal(orderNumbers[0], orders.Items[0].OrderNumber); // the oldest is on the last page

            var standing = await services.GetRequiredService<IStandingOrderRepository>().SearchAsync(null, null, page, 20);
            Assert.Equal((1, standingOrder), (standing.Page, Assert.Single(standing.Items).Id));

            var inquiries = await services.GetRequiredService<ICateringInquiryRepository>().ListAsync(false, page, 20);
            Assert.Equal((1, 1), (inquiries.Page, inquiries.Items.Count));

            var purchases = await services.GetRequiredService<IInventoryRepository>().SearchPurchasesAsync(new InventoryPurchaseSearch { Search = "Pager", Page = page });
            Assert.Equal((1, 1), (purchases.Purchases.Page, purchases.Purchases.Items.Count));

            var emails = await services.GetRequiredService<IEmailLog>().ListAsync(null, page, 1);
            Assert.True(emails.TotalCount > 1);
            Assert.Equal((Math.Min(page, emails.TotalPages), 1), (emails.Page, emails.Items.Count));

            var found = await shifts.SearchAsync(new ShiftSearch { UserId = employee.UserId, Page = page, PageSize = 20 });
            Assert.Equal((1, 1), (found.Page, found.Items.Count));
        }

        // No rows at all: the first page, whatever was asked for.
        var none = await services.GetRequiredService<IOrderRepository>().SearchAsync(new OrderSearchCriteria { Search = "nobody at all", Page = int.MaxValue });
        Assert.Equal((1, 0), (none.Page, none.TotalCount));

        // The pages themselves, as an old link or a typed number would open them.
        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        foreach (var url in new[]
                 {
                     "/admin/orders?p=100000000", "/admin/inventory?p=100000000", "/admin/emails?p=100000000",
                     "/admin/recurring?status=all&p=120000001", "/admin/inquiries?p=120000001", "/admin/customers?p=2147483647",
                     $"/admin/time-clock/team?user={employee.UserId}&p=2147483647",
                 })
        {
            var response = await admin.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {(int)response.StatusCode}");
        }

        Assert.Contains(orderNumbers[2], await admin.GetStringAsync("/admin/orders?p=100000000"));
    }

    private static async Task<string> PlaceOrderAsync(AppFactory app, string name, string email)
    {
        using var scope = app.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var checkout = await sender.Send(new GetCheckoutOptionsQuery());
        var menu = await sender.Send(new GetMenuQuery());
        var placed = await sender.Send(new PlaceOrderCommand
        {
            CustomerName = name,
            Email = email,
            Phone = "214-555-0199",
            Fulfillment = FulfillmentMethod.Pickup,
            ScheduledDate = checkout.Days[0].Date,
            ScheduledTime = checkout.Days[0].Slots[0],
            Lines = [new CartLineInput(menu[0].Id, 1m)],
        });
        return placed.OrderNumber;
    }

    private static async Task<int> AddStandingOrderAsync(IServiceProvider services, string email)
    {
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var item = (await services.GetRequiredService<IMenuItemRepository>().GetBySlugAsync("loitta-shutki-vorta"))!;
        var order = StandingOrder.Submit(
            $"RO-{Guid.NewGuid():N}"[..16], null, "Paging Bistro", new CustomerContact("Chef", email, "(214) 555-0142"), null,
            FulfillmentMethod.Pickup, null, WeekDays.EveryDay, new TimeOnly(10, 0), DateOnly.FromDateTime(clock.BusinessNow).AddDays(2), null, null,
            [new StandingOrderLineRequest(item.Id, item.Name, item.BengaliName, item.Unit, 20m, 6m)], 0m, new WholesaleRules(5m, 1m, 100m), clock.UtcNow);
        await services.GetRequiredService<IStandingOrderRepository>().AddAsync(order);
        return order.Id;
    }

    private static async Task<int> AddPurchaseAsync(IServiceProvider services, string store, string itemName)
    {
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var today = DateOnly.FromDateTime(clock.BusinessNow);
        var purchase = InventoryPurchase.Record(today, store, null, [new InventoryLineRequest(itemName, 1m, "bag", 10m)], "tests", today, clock.UtcNow);
        await services.GetRequiredService<IInventoryRepository>().AddPurchaseAsync(purchase);
        return purchase.Id;
    }
}
