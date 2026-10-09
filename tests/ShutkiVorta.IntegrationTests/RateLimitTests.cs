using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using ShutkiVorta.IntegrationTests.Infrastructure;

namespace ShutkiVorta.IntegrationTests;

/// <summary>The public forms that send emails cannot be submitted endlessly from one address.</summary>
public sealed class RateLimitTests : IAsyncLifetime
{
    private readonly AppFactory _app = new("Sqlite", new Dictionary<string, string?> { ["RateLimiting:FormPostsPerWindow"] = "2" });

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task FormPosts_AreLimitedPerForm_WhilePagesStayAvailable()
    {
        var client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://shutki.test") });
        HttpResponseMessage Post(string path) => client.PostAsync(path, new FormUrlEncodedContent([])).GetAwaiter().GetResult();

        Assert.NotEqual(HttpStatusCode.TooManyRequests, Post("/kitchen").StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, Post("/kitchen").StatusCode);
        var limited = Post("/kitchen");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Contains("try again", await limited.Content.ReadAsStringAsync());

        Assert.NotEqual(HttpStatusCode.TooManyRequests, Post("/contact").StatusCode); // each form has its own allowance
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/kitchen")).StatusCode); // reading pages is never limited
    }
}
