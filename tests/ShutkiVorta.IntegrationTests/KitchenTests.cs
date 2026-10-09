using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ShutkiVorta.Application.Features.Inquiries;
using ShutkiVorta.Domain.Inquiries;
using ShutkiVorta.IntegrationTests.Infrastructure;

namespace ShutkiVorta.IntegrationTests;

/// <summary>"Our Kitchen" page: content, SEO and the "join our kitchen" form for homemakers.</summary>
[Collection("app")]
public sealed partial class KitchenTests(TestServers servers)
{
    private HttpClient Client(bool followRedirects = true) =>
        servers.Get("Sqlite").CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = followRedirects, BaseAddress = new Uri("https://shutki.test") });

    [Fact]
    public async Task KitchenPage_Renders_WithSeoAndJoinForm()
    {
        var response = await Client().GetAsync("/kitchen");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("মায়ের হাতের স্বাদ", html);
        Assert.Contains("Made by Bangladeshi mothers", html);
        Assert.Contains("<link rel=\"canonical\" href=\"https://shutki.test/kitchen\" />", html);
        Assert.Contains("\"@type\":\"BreadcrumbList\"", html);
        Assert.Contains("action=\"/kitchen#join\"", html);
        Assert.Contains("name=\"Input.Website\"", html); // honeypot
    }

    [Theory]
    [InlineData("/", "Made by Bangladeshi mothers")]
    [InlineData("/about", "Mothers in our kitchen")]
    public async Task HomeAndStoryPages_PromoteTheKitchen(string path, string heading)
    {
        var html = await Client().GetStringAsync(path);
        Assert.Contains($">{heading}</h2>", html);
        Assert.Contains("class=\"btn btn-green\" href=\"/kitchen\"", html);
        Assert.Contains("/css/kitchen.css?v=", html);
    }

    [Fact]
    public async Task JoinForm_CreatesJoinKitchenInquiry_AndEmailsKitchenAndApplicant()
    {
        var factory = servers.Get("Sqlite");
        var client = Client();
        var page = await client.GetStringAsync("/kitchen");
        var email = $"cook-{Guid.NewGuid():N}@test.local";
        var marker = $"marker-{Guid.NewGuid():N}";

        var response = await client.PostAsync("/kitchen", Form(page, new()
        {
            ["Input.Name"] = "Test Homemaker",
            ["Input.Phone"] = "(214) 555-0199",
            ["Input.Email"] = email,
            ["Input.Message"] = $"I make loitta shutki vorta the Chattogram way and live in Irving. {marker}",
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/kitchen", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("sent=true", response.RequestMessage.RequestUri.Query);
        Assert.Contains("Thank you for reaching out", await response.Content.ReadAsStringAsync());

        using (var scope = factory.Services.CreateScope())
        {
            var inquiries = await scope.ServiceProvider.GetRequiredService<ICateringInquiryRepository>().ListAsync(onlyOpen: true, page: 1, pageSize: 200);
            var inquiry = Assert.Single(inquiries.Items, i => i.Email == email);
            Assert.Equal(InquiryTopic.JoinKitchen, inquiry.Topic);
            Assert.Equal("Test Homemaker", inquiry.Name);
            Assert.Equal("(214) 555-0199", inquiry.Phone);
            Assert.Null(inquiry.EventDate);
        }

        var mails = await WaitForMailAsync(factory, marker, expected: 2);
        Assert.Contains(mails, m => m.Contains("To: kitchen@test.local") && m.Contains("Test Homemaker would like to cook with us"));
        Assert.Contains(mails, m => m.Contains($"To: {email}") && m.Contains("Thank you for wanting to share your hands"));
    }

    [Fact]
    public async Task JoinForm_RequiresAPhoneNumber()
    {
        var client = Client();
        var page = await client.GetStringAsync("/kitchen");

        var response = await client.PostAsync("/kitchen", Form(page, new()
        {
            ["Input.Name"] = "No Phone",
            ["Input.Phone"] = "",
            ["Input.Email"] = "no.phone@test.local",
            ["Input.Message"] = "Hello",
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Please share a phone number so we can call you.", html);
        Assert.DoesNotContain("Thank you for reaching out", html);
    }

    [Fact]
    public async Task JoinForm_SilentlyDropsHoneypotSubmissions()
    {
        var client = Client(followRedirects: false);
        var page = await client.GetStringAsync("/kitchen");
        var email = $"bot-{Guid.NewGuid():N}@test.local";

        var response = await client.PostAsync("/kitchen", Form(page, new()
        {
            ["Input.Name"] = "Bot",
            ["Input.Phone"] = "(214) 555-0100",
            ["Input.Email"] = email,
            ["Input.Message"] = "Buy cheap things",
            ["Input.Website"] = "https://spam.example",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/kitchen?sent=true#join", response.Headers.Location!.ToString());

        using var scope = servers.Get("Sqlite").Services.CreateScope();
        var inquiries = await scope.ServiceProvider.GetRequiredService<ICateringInquiryRepository>().ListAsync(onlyOpen: false, page: 1, pageSize: 200);
        Assert.DoesNotContain(inquiries.Items, i => i.Email == email);
    }

    private static FormUrlEncodedContent Form(string page, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = AntiforgeryToken().Match(page).Groups[1].Value;
        return new FormUrlEncodedContent(fields);
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
                return mails;
            }

            await Task.Delay(200);
        }

        throw new Xunit.Sdk.XunitException($"Expected {expected} emails mentioning '{containing}' in {factory.MailDirectory}.");
    }

    private static string ToHeader(string emlPath) =>
        File.Exists(emlPath) ? File.ReadLines(emlPath).FirstOrDefault(l => l.StartsWith("To:", StringComparison.OrdinalIgnoreCase)) ?? string.Empty : string.Empty;

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
