using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using ShutkiVorta.IntegrationTests.Infrastructure;

namespace ShutkiVorta.IntegrationTests;

/// <summary>HTTP-level tests of the public site, SEO endpoints, security and the checkout flow.</summary>
[Collection("app")]
public sealed partial class WebTests(TestServers servers)
{
    private HttpClient Client(bool followRedirects = true) =>
        servers.Get("Sqlite").CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = followRedirects, BaseAddress = new Uri("https://shutki.test") });

    [Theory]
    [InlineData("/")]
    [InlineData("/menu")]
    [InlineData("/menu/loitta-shutki-vorta")]
    [InlineData("/menu/aloo-vorta")]
    [InlineData("/catering")]
    [InlineData("/about")]
    [InlineData("/faq")]
    [InlineData("/contact")]
    [InlineData("/privacy")]
    [InlineData("/cart")]
    [InlineData("/track-order")]
    [InlineData("/account/login")]
    [InlineData("/account/register")]
    public async Task PublicPages_Render(string path)
    {
        var response = await Client().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Shutki Vorta", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MenuItemPage_HasSeoMetadata()
    {
        var html = await Client().GetStringAsync("/menu/chingri-shutki-vorta");

        Assert.Contains("<link rel=\"canonical\" href=\"https://shutki.test/menu/chingri-shutki-vorta\" />", html);
        Assert.Contains("<meta name=\"description\"", html);
        Assert.Contains("<script type=\"application/ld+json\">", html);
        Assert.Contains("\"@type\":\"Product\"", html);
        Assert.Contains("\"unitCode\":\"LBR\"", html);
        Assert.Contains("<h1>Chingri Shutki Vorta</h1>", html);
    }

    [Fact]
    public async Task UnknownMenuItem_Returns404Page()
    {
        var response = await Client().GetAsync("/menu/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("couldn&#x27;t find", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MenuItemSlug_IsCanonicalised()
    {
        var response = await Client(followRedirects: false).GetAsync("/menu/Aloo-Vorta");
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.EndsWith("/menu/aloo-vorta", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task RobotsAndSitemap_AreGeneratedFromTheMenu()
    {
        var client = Client();
        var robots = await client.GetAsync("/robots.txt");
        var robotsText = await robots.Content.ReadAsStringAsync();
        Assert.Equal("text/plain", robots.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Allow: /menu/loitta-shutki-vorta", robotsText);
        Assert.Contains("Disallow: /admin", robotsText);
        Assert.Contains("Sitemap: https://shutki.test/sitemap.xml", robotsText);

        var sitemap = await client.GetStringAsync("/sitemap.xml");
        Assert.Contains("<loc>https://shutki.test/menu/shim-vorta</loc>", sitemap);
        Assert.Contains("<image:loc>https://shutki.test/images/menu/shim-vorta.webp</image:loc>", sitemap);
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/orders")]
    [InlineData("/account/orders")]
    public async Task ProtectedPages_RedirectToLogin(string path)
    {
        var response = await Client(followRedirects: false).GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Admin_CanSignInAndSeeDashboard()
    {
        var client = Client();
        var login = await client.GetStringAsync("/account/login");
        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = AppFactory.AdminEmail,
            ["Input.Password"] = AppFactory.AdminPassword,
            ["__RequestVerificationToken"] = Token(login),
        }));
        response.EnsureSuccessStatusCode();

        var dashboard = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        var html = await dashboard.Content.ReadAsStringAsync();
        Assert.Contains("Upcoming pickups", html);
        Assert.Contains("noindex", html);
    }

    [Fact]
    public async Task Guest_CanAddToCartAndCheckOut()
    {
        var client = Client();

        // Add 1.5 lb to the cart from the item page form.
        var itemPage = await client.GetStringAsync("/menu/begun-vorta");
        var menuItemId = MenuItemIdInput().Match(itemPage).Groups[1].Value;
        var added = await client.PostAsync("/cart?handler=Add", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["menuItemId"] = menuItemId,
            ["quantity"] = "1.5",
            ["returnUrl"] = "/menu/begun-vorta",
            ["__RequestVerificationToken"] = Token(itemPage),
        }));
        added.EnsureSuccessStatusCode();
        Assert.Contains("Begun Vorta", await client.GetStringAsync("/cart"));

        // Check out for pickup on the first available slot.
        var checkout = await client.GetStringAsync("/checkout");
        var date = FirstOption("Input_ScheduledDate").Match(checkout).Groups[1].Value;
        var time = FirstOption("Input_ScheduledTime").Match(checkout).Groups[1].Value;

        var placed = await client.PostAsync("/checkout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.CustomerName"] = "Web Tester",
            ["Input.Email"] = "web.tester@test.local",
            ["Input.Phone"] = "(214) 555-0142",
            ["Input.Fulfillment"] = "Pickup",
            ["Input.ScheduledDate"] = date,
            ["Input.ScheduledTime"] = time,
            ["Input.Notes"] = "Integration test",
            ["__RequestVerificationToken"] = Token(checkout),
        }));

        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        Assert.Contains("/thank-you", placed.RequestMessage!.RequestUri!.AbsolutePath);
        var thankYou = await placed.Content.ReadAsStringAsync();
        Assert.Contains("ধন্যবাদ", thankYou);
        Assert.Contains("$20.99", thankYou); // 1.5 lb × $13.99
        Assert.Contains("$22.72", thankYou); // + 8.25% Dallas sales tax
        Assert.Contains("Your plate is empty", await client.GetStringAsync("/cart"));
    }

    private static string Token(string html) => AntiforgeryToken().Match(html).Groups[1].Value;

    private static Regex FirstOption(string selectId) =>
        new($"id=\"{selectId}\"[^>]*>\\s*<option value=\"([^\"]+)\"", RegexOptions.Singleline);

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    [GeneratedRegex("name=\"menuItemId\" value=\"(\\d+)\"")]
    private static partial Regex MenuItemIdInput();
}
