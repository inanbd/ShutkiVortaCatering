using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Application.Features.Wholesale;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.Domain.Wholesale;
using ShutkiVorta.IntegrationTests.Infrastructure;

namespace ShutkiVorta.IntegrationTests;

/// <summary>The admin side of restaurant standing orders: review, approve, change terms, skip dates and reporting pages.</summary>
[Collection("app")]
public sealed partial class AdminRestaurantTests(TestServers servers)
{
    [Theory]
    [InlineData("/admin/recurring")]
    [InlineData("/admin/recurring/1")]
    [InlineData("/admin/recurring/1/statement")]
    [InlineData("/admin/production")]
    public async Task AdminRestaurantPages_RequireSignIn(string path)
    {
        var client = servers.Get("Sqlite").CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://shutki.test") });
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Admin_ApprovesChangesTermsAndSkipsDates_AndReportsSeparateRestaurantFromOnlineSales()
    {
        var factory = servers.Get("Sqlite");
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var standingOrders = services.GetRequiredService<IStandingOrderRepository>();
        var orders = services.GetRequiredService<IOrderRepository>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var item = (await services.GetRequiredService<IMenuItemRepository>().GetBySlugAsync("shim-vorta"))!;
        var testStartUtc = clock.UtcNow.AddSeconds(-1);

        // A restaurant's request, awaiting approval.
        var name = $"Rupchanda Grill {Guid.NewGuid().ToString("N")[..6]}";
        var today = DateOnly.FromDateTime(clock.BusinessNow);
        var start = today.AddDays(2);
        var request = StandingOrder.Submit(
            $"RO-ADM-{Guid.NewGuid():N}"[..16], null, name, new CustomerContact("Rina Das", "rina@rupchanda.test", "(214) 555-0177"), "32099887766",
            FulfillmentMethod.Pickup, null, WeekDays.EveryDay, new TimeOnly(11, 0), start, null, "Side entrance please.",
            [new StandingOrderLineRequest(item.Id, item.Name, item.BengaliName, item.Unit, 15m, 10m)], 0m, new WholesaleRules(5m, 1m, 100m), clock.UtcNow);
        await standingOrders.AddAsync(request);
        var id = request.Id;

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://shutki.test") });
        await SignInAsAdminAsync(client);

        var list = await client.GetStringAsync("/admin/recurring?status=PendingApproval");
        Assert.Contains(name, list);
        Assert.Contains("Generate upcoming deliveries now", list);

        var details = await client.GetStringAsync($"/admin/recurring/{id}");
        Assert.Contains("Awaiting approval", details);
        Assert.Contains("Side entrance please.", details);
        Assert.Contains("name=\"Input.Lines[0].UnitPrice\"", details);

        // Approve: orders for the next few days are generated straight away.
        var approved = await PostAsync(client, $"/admin/recurring/{id}?handler=Status", details, new()
        {
            ["change"] = "Approve",
            ["note"] = "Welcome aboard",
            ["notifyRestaurant"] = "false",
        });
        Assert.Contains("upcoming deliveries have been scheduled", approved);
        Assert.Contains("Pause deliveries", approved);

        var range = (From: today.ToDateTime(TimeOnly.MinValue), To: today.AddDays(30).ToDateTime(TimeOnly.MaxValue));
        var generated = await orders.GetForStandingOrderAsync(id, range.From, range.To);
        Assert.NotEmpty(generated);
        Assert.All(generated, o => Assert.Equal(150m, o.Subtotal));

        // The orders list separates restaurant orders from online ones, and the order links back to its standing order.
        var restaurantOrders = await client.GetStringAsync($"/admin/orders?source=Restaurant&q={Uri.EscapeDataString(name)}");
        Assert.Contains(generated[0].OrderNumber, restaurantOrders);
        Assert.Contains("source-badge", restaurantOrders);
        Assert.Contains("No orders match", await client.GetStringAsync($"/admin/orders?source=Online&q={Uri.EscapeDataString(name)}"));
        var orderPage = await client.GetStringAsync($"/admin/orders/{generated[0].OrderNumber}");
        Assert.Contains("Restaurant standing order", orderPage);
        Assert.Contains($"href=\"/admin/recurring/{id}\"", orderPage);

        // Change the agreed price and quantity: open orders are re-issued with the new terms.
        details = await client.GetStringAsync($"/admin/recurring/{id}");
        var saved = await PostAsync(client, $"/admin/recurring/{id}?handler=Terms", details, new()
        {
            ["Input.Lines[0].MenuItemId"] = item.Id.ToString(CultureInfo.InvariantCulture),
            ["Input.Lines[0].Quantity"] = "8",
            ["Input.Lines[0].UnitPrice"] = "19.50",
            ["Input.Days"] = "Tuesday",
            ["Input.PreferredTime"] = "11:00",
            ["Input.StartDate"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Input.DeliveryFee"] = "0",
            ["Input.TaxExempt"] = "true",
            ["Input.AdminNotes"] = "Certificate on file",
        });
        Assert.Contains("Terms saved", saved);
        var updated = (await standingOrders.GetByIdAsync(id))!;
        var line = Assert.Single(updated.Lines);
        Assert.Equal(8m, line.Quantity);
        Assert.Equal(19.50m, line.UnitPrice);
        Assert.True(updated.TaxExempt);
        Assert.Equal(WeekDays.Tuesday, updated.DaysOfWeek);
        var open = (await orders.GetForStandingOrderAsync(id, range.From, range.To)).Where(o => o.Status != OrderStatus.Cancelled).ToList();
        Assert.All(open, o =>
        {
            Assert.Equal(156m, o.Subtotal);
            Assert.Equal(0m, o.Tax);
            Assert.Equal(DayOfWeek.Tuesday, o.ScheduledFor.DayOfWeek);
        });

        // Invalid terms stay on the page with the error.
        var invalid = await PostAsync(client, $"/admin/recurring/{id}?handler=Terms", saved, new()
        {
            ["Input.Lines[0].MenuItemId"] = item.Id.ToString(CultureInfo.InvariantCulture),
            ["Input.Lines[0].Quantity"] = "2",
            ["Input.Days"] = "Tuesday",
            ["Input.PreferredTime"] = "11:00",
            ["Input.StartDate"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Input.DeliveryFee"] = "0",
        });
        Assert.Contains("The terms were not saved", invalid);
        Assert.Contains("the minimum for restaurant orders is 5 lb", invalid);

        // Skip the next delivery, then restore it.
        var nextTuesday = Enumerable.Range(2, 7).Select(today.AddDays).First(d => d.DayOfWeek == DayOfWeek.Tuesday);
        var skipped = await PostAsync(client, $"/admin/recurring/{id}?handler=Skip", saved, new()
        {
            ["date"] = nextTuesday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["skip"] = "true",
        });
        Assert.Contains("was skipped", skipped);
        Assert.Contains("Restore", skipped);
        var restored = await PostAsync(client, $"/admin/recurring/{id}?handler=Skip", skipped, new()
        {
            ["date"] = nextTuesday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["skip"] = "false",
        });
        Assert.Contains("was restored", restored);

        // Reporting pages.
        var statement = await client.GetStringAsync(
            $"/admin/recurring/{id}/statement?from={today:yyyy-MM-dd}&to={today.AddDays(30):yyyy-MM-dd}");
        Assert.Contains(name, statement);
        Assert.Contains("Amount for this period", statement);
        var production = await client.GetStringAsync($"/admin/production?from={today:yyyy-MM-dd}&days=14");
        Assert.Contains(item.Name, production);
        Assert.Contains("Projected deliveries", production);
        var dashboard = await client.GetStringAsync("/admin");
        Assert.Contains("Restaurant requests", dashboard);
        Assert.Contains("Restaurant deliveries this week", dashboard);
        Assert.Contains("Wholesale price per lb", await client.GetStringAsync($"/admin/menu/edit/{item.Id}"));

        // Online-only sales figures never include restaurant orders.
        Assert.All((await orders.SearchAsync(new OrderSearchCriteria { Source = OrderSource.Restaurant, PageSize = 100 })).Items,
            o => Assert.NotNull(o.StandingOrderId));
        Assert.All((await orders.SearchAsync(new OrderSearchCriteria { Source = OrderSource.Online, PageSize = 100 })).Items,
            o => Assert.Null(o.StandingOrderId));
        var all = await orders.GetPopularItemsSinceAsync(testStartUtc, 100);
        var online = await orders.GetPopularItemsSinceAsync(testStartUtc, 100, OrderSource.Online);
        var restaurant = await orders.GetPopularItemsSinceAsync(testStartUtc, 100, OrderSource.Restaurant);
        if (open.Count > 0)
        {
            Assert.Contains(restaurant, p => p.ItemName == item.Name);
        }

        foreach (var popular in all)
        {
            Assert.Equal(
                popular.TotalQuantity,
                (online.FirstOrDefault(p => p.ItemName == popular.ItemName)?.TotalQuantity ?? 0) +
                (restaurant.FirstOrDefault(p => p.ItemName == popular.ItemName)?.TotalQuantity ?? 0));
        }
    }

    private static async Task SignInAsAdminAsync(HttpClient client)
    {
        var login = await client.GetStringAsync("/account/login");
        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = AppFactory.AdminEmail,
            ["Input.Password"] = AppFactory.AdminPassword,
            ["__RequestVerificationToken"] = Token(login),
        }));
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Posts a form using the antiforgery token from <paramref name="page"/> and returns the page it redirects to.</summary>
    private static async Task<string> PostAsync(HttpClient client, string url, string page, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = Token(page);
        var response = await client.PostAsync(url, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static string Token(string html) => AntiforgeryToken().Match(html).Groups[1].Value;

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
