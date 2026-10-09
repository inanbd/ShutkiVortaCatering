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

/// <summary>Restaurant standing orders: the order-generation ledger on every database provider, and the web flow.</summary>
[Collection("app")]
public sealed partial class RestaurantTests(TestServers servers)
{
    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task Scheduler_GeneratesIdempotently_AndReconcilesSkipsPausesAndResumes(string provider)
    {
        using var scope = servers.Get(provider).Services.CreateScope();
        var services = scope.ServiceProvider;
        var repository = services.GetRequiredService<IStandingOrderRepository>();
        var orders = services.GetRequiredService<IOrderRepository>();
        var scheduler = services.GetRequiredService<StandingOrderScheduler>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var item = (await services.GetRequiredService<IMenuItemRepository>().GetBySlugAsync("loitta-shutki-vorta"))!;

        var today = DateOnly.FromDateTime(clock.BusinessNow);
        var standingOrder = StandingOrder.Submit(
            $"RO-TEST-{Guid.NewGuid():N}"[..16],
            null,
            "Ledger Test Bistro",
            new CustomerContact("Test Chef", "chef@ledger.test", "(214) 555-0142"),
            null,
            FulfillmentMethod.Pickup,
            null,
            WeekDays.EveryDay,
            new TimeOnly(10, 0),
            today.AddDays(2),
            null,
            null,
            [new StandingOrderLineRequest(item.Id, item.Name, item.BengaliName, item.Unit, 20m, 6m)],
            0m,
            new WholesaleRules(5m, 1m, 100m),
            clock.UtcNow);
        await repository.AddAsync(standingOrder);
        standingOrder.Approve("test", null, clock.UtcNow);
        standingOrder.UpdateTerms(
            [new StandingOrderLineRequest(item.Id, item.Name, item.BengaliName, item.Unit, 20m, 6m)],
            WeekDays.EveryDay, new TimeOnly(10, 0), today.AddDays(2), null, 0m, taxExempt: true, new WholesaleRules(5m, 1m, 100m), "test", clock.UtcNow);
        await repository.UpdateAsync(standingOrder);

        var so = (await repository.GetByIdAsync(standingOrder.Id))!;
        Assert.Equal(StandingOrderStatus.Active, so.Status);
        Assert.True(so.TaxExempt);
        Assert.Single(so.Lines);
        Assert.Equal(3, so.Events.Count);

        // Generate: every day from today+2 to the 7-day horizon is either an order or a "kitchen closed" skip.
        var first = await scheduler.GenerateAsync(so);
        Assert.Equal(6, first.Generated + first.Skipped);
        Assert.True(first.Generated >= 5);
        var second = await scheduler.GenerateAsync(so);
        Assert.Equal(0, second.Generated + second.Skipped); // idempotent

        var range = (from: today.ToDateTime(TimeOnly.MinValue), to: today.AddDays(30).ToDateTime(TimeOnly.MaxValue));
        var generated = await orders.GetForStandingOrderAsync(so.Id, range.from, range.to);
        Assert.Equal(first.Generated, generated.Count);
        Assert.All(generated, o =>
        {
            Assert.Equal(OrderStatus.Confirmed, o.Status);
            Assert.Equal("Ledger Test Bistro", o.CompanyName);
            Assert.Equal(120m, o.Subtotal);
            Assert.Equal(0m, o.Tax); // tax exempt
        });
        Assert.False(await repository.TryAddOccurrenceAsync(so.Id, DateOnly.FromDateTime(generated[0].ScheduledFor), OccurrenceStatus.Generated, null));

        // Skip one delivery: its order is cancelled and the date stays skipped on the next run.
        var skipDate = DateOnly.FromDateTime(generated[0].ScheduledFor);
        await scheduler.SkipDateAsync(so, skipDate, "Test Chef");
        var skippedOrder = (await orders.GetByNumberAsync(generated[0].OrderNumber))!;
        Assert.Equal(OrderStatus.Cancelled, skippedOrder.Status);
        Assert.Equal(0, (await scheduler.GenerateAsync(so)).Generated);
        var upcoming = await scheduler.GetUpcomingAsync(so, 10);
        var skipped = upcoming.Single(u => u.Date == skipDate);
        Assert.Equal(DeliveryState.Skipped, skipped.State);
        Assert.True(skipped.CanUnskip);

        // Restore it: a fresh order is generated for that date.
        await scheduler.UnskipDateAsync(so, skipDate, "Test Chef");
        var restored = (await orders.GetForStandingOrderAsync(so.Id, range.from, range.to))
            .Where(o => DateOnly.FromDateTime(o.ScheduledFor) == skipDate).ToList();
        Assert.Equal(2, restored.Count);
        Assert.Single(restored, o => o.Status == OrderStatus.Confirmed);

        // Pause withdraws every open generated order; resume brings them back.
        so.Pause("Test Chef", "Renovation", clock.UtcNow);
        await repository.UpdateAsync(so);
        var (withdrawn, locked) = await scheduler.WithdrawUpcomingAsync(so, null, OccurrenceStatus.Cancelled, "Paused", "Test Chef");
        Assert.Equal(first.Generated, withdrawn);
        Assert.Equal(0, locked);
        Assert.DoesNotContain(await orders.GetForStandingOrderAsync(so.Id, range.from, range.to), o => o.Status == OrderStatus.Confirmed);
        Assert.Equal(0, (await scheduler.GenerateAsync(so)).Generated); // paused

        so.Resume("Test Chef", clock.UtcNow);
        await repository.UpdateAsync(so);
        await scheduler.ReopenWithdrawnDatesAsync(so);
        Assert.Equal(first.Generated, (await scheduler.GenerateAsync(so)).Generated);

        Assert.Contains(await repository.GetByStatusAsync(StandingOrderStatus.Active), s => s.Id == so.Id);
        var search = await repository.SearchAsync(StandingOrderStatus.Active, "Ledger Test", 1, 20);
        var row = Assert.Single(search.Items, s => s.Id == so.Id);
        Assert.Equal(120m, row.SubtotalPerDelivery);
        Assert.Contains("Loitta Shutki Vorta", row.ItemsPreview);
    }

    [Fact]
    public async Task Restaurant_CanRegisterAndRequestAStandingOrder()
    {
        var factory = servers.Get("Sqlite");
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://shutki.test") });

        var landing = await client.GetStringAsync("/restaurants");
        Assert.Contains("Wholesale price list", landing);
        Assert.Contains("<link rel=\"canonical\" href=\"https://shutki.test/restaurants\" />", landing);

        var email = $"chef.{Guid.NewGuid():N}@bistro.test";
        var register = await client.GetStringAsync("/account/register");
        var registered = await client.PostAsync("/account/register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.FullName"] = "Kamal Hossain",
            ["Input.Email"] = email,
            ["Input.Phone"] = "(214) 555-0142",
            ["Input.Password"] = "Restaurant!2026",
            ["Input.ConfirmPassword"] = "Restaurant!2026",
            ["__RequestVerificationToken"] = Token(register),
        }));
        registered.EnsureSuccessStatusCode();

        var form = await client.GetStringAsync("/restaurants/order");
        Assert.Contains("noindex", form);
        var itemIds = QuantityInput().Matches(form).Select(m => m.Groups[1].Value).ToList();
        var startDate = MinAttribute().Match(StartDateInput().Match(form).Value).Groups[1].Value;
        var time = FirstTimeOption().Match(form).Groups[1].Value;

        var posted = await client.PostAsync("/restaurants/order", new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
        {
            new("Input.BusinessName", "Spice Garden Bistro"),
            new("Input.ContactName", "Kamal Hossain"),
            new("Input.Email", email),
            new("Input.Phone", "(214) 555-0142"),
            new($"Input.Quantities[{itemIds[0]}]", "10"),
            new($"Input.Quantities[{itemIds[3]}]", "6"),
            new("Input.Days", "Tuesday"),
            new("Input.Days", "Friday"),
            new("Input.PreferredTime", time),
            new("Input.StartDate", startDate),
            new("Input.Fulfillment", "Delivery"),
            new("Input.AddressLine1", "2800 Routh St"),
            new("Input.City", "Dallas"),
            new("Input.State", "TX"),
            new("Input.PostalCode", "75201"),
            new("__RequestVerificationToken", Token(form)),
        }));

        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        var postedHtml = await posted.Content.ReadAsStringAsync();
        Assert.True(posted.RequestMessage!.RequestUri!.AbsolutePath.StartsWith("/account/restaurant-orders/RO-"),
            posted.RequestMessage.RequestUri + " " + string.Join(" | ", Regex.Matches(postedHtml, "<(?:span|li)[^>]*>([^<]*(?:Please|must|minimum|earliest|valid)[^<]*)<").Select(m => m.Groups[1].Value)));
        Assert.StartsWith("/account/restaurant-orders/RO-", posted.RequestMessage!.RequestUri!.AbsolutePath);
        var details = await posted.Content.ReadAsStringAsync();
        Assert.Contains("We received your request", details);
        Assert.Contains("Awaiting approval", details);
        Assert.Contains("Tue, Fri", details);

        var list = await client.GetStringAsync("/account/restaurant-orders");
        Assert.Contains("Spice Garden Bistro", list);

        // The kitchen and the restaurant are both emailed, and the layout keeps the kitchen's own name.
        var mails = await WaitForMailAsync(factory, "Spice Garden Bistro", expected: 2);
        Assert.Contains(mails, m => m.Contains("kitchen@test.local") && m.Contains("Review &amp; approve"));
        Assert.Contains(mails, m => m.Contains(email) && m.Contains("We received your request"));
    }

    [Fact]
    public async Task StandingOrderForm_RequiresSignIn_AndIsHiddenFromSearchEngines()
    {
        var client = servers.Get("Sqlite").CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://shutki.test") });

        foreach (var path in new[] { "/restaurants/order", "/account/restaurant-orders" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/account/login", response.Headers.Location!.ToString());
        }

        Assert.Contains("Disallow: /restaurants/order", await client.GetStringAsync("/robots.txt"));
        var sitemap = await client.GetStringAsync("/sitemap.xml");
        Assert.Contains("<loc>https://shutki.test/restaurants</loc>", sitemap);
        Assert.Contains("<loc>https://shutki.test/kitchen</loc>", sitemap);
    }

    private static async Task<List<string>> WaitForMailAsync(AppFactory factory, string containing, int expected)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            // Each email is saved as .html (body) plus .eml (with headers); prefix the body with its To: header.
            var mails = Directory.Exists(factory.MailDirectory)
                ? Directory.GetFiles(factory.MailDirectory, "*.html")
                    .Select(html => ToHeader(Path.ChangeExtension(html, ".eml")) + "\n" + File.ReadAllText(html))
                    .Where(m => m.Contains(containing))
                    .ToList()
                : [];
            if (mails.Count >= expected)
            {
                Assert.All(mails, m => Assert.Contains("Shutki Vorta Catering", m));
                return mails;
            }

            await Task.Delay(200);
        }

        throw new Xunit.Sdk.XunitException($"Expected {expected} emails mentioning '{containing}' in {factory.MailDirectory}.");
    }

    private static string ToHeader(string emlPath) =>
        File.Exists(emlPath) ? File.ReadLines(emlPath).FirstOrDefault(l => l.StartsWith("To:", StringComparison.OrdinalIgnoreCase)) ?? string.Empty : string.Empty;

    private static string Token(string html) => AntiforgeryToken().Match(html).Groups[1].Value;

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    [GeneratedRegex("name=\"Input.Quantities\\[(\\d+)\\]\"")]
    private static partial Regex QuantityInput();

    [GeneratedRegex("<input[^>]*id=\"Input_StartDate\"[^>]*>")]
    private static partial Regex StartDateInput();

    [GeneratedRegex("min=\"([0-9-]+)\"")]
    private static partial Regex MinAttribute();

    [GeneratedRegex("id=\"Input_PreferredTime\"[^>]*>\\s*<option value=\"([^\"]+)\"", RegexOptions.Singleline)]
    private static partial Regex FirstTimeOption();
}
