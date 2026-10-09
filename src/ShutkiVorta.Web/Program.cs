using System.Globalization;
using MediatR;
using Microsoft.Net.Http.Headers;
using ShutkiVorta.Application;
using ShutkiVorta.Application.Features.Seo;
using ShutkiVorta.Infrastructure;
using ShutkiVorta.Infrastructure.Settings;
using ShutkiVorta.Web;
using ShutkiVorta.Web.Infrastructure;

var culture = CultureInfo.GetCultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

var builder = WebApplication.CreateBuilder(args);

// Business settings (Admin → Settings) are stored in the database; appsettings.json only holds server settings.
var settings = SettingsConfiguration.Create();

builder.Services.AddApplication(settings.Root);
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment, settings);
builder.Services.AddWebServices(builder.Configuration, builder.Environment);

// Catch lifetime mistakes (e.g. a singleton holding per-request settings) in every environment, not just Development.
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

var app = builder.Build();

// Create/upgrade the database (SQLite or SQL Server) and seed roles, the admin account and the starter menu.
await app.Services.InitializeDatabaseAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/error/{0}");

if (app.Configuration.GetValue("UseHttpsRedirection", true))
{
    app.UseHttpsRedirection();
}

app.UseResponseCompression();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Versioned assets (asp-append-version) and images can be cached for a long time.
        var maxAge = ctx.Context.Request.Query.ContainsKey("v") ? 31536000 : 604800;
        ctx.Context.Response.Headers[HeaderNames.CacheControl] = $"public,max-age={maxAge}";
    },
});

app.UseRequestLocalization();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ApplicationExceptionMiddleware>();

// SEO endpoints — regenerated automatically whenever the menu changes.
app.MapGet("/robots.txt", async (ISender sender, CancellationToken ct) =>
    Results.Text(await sender.Send(new GetRobotsTxtQuery(), ct), "text/plain; charset=utf-8"));
app.MapGet("/sitemap.xml", async (ISender sender, CancellationToken ct) =>
    Results.Text(await sender.Send(new GetSitemapQuery(), ct), "application/xml; charset=utf-8"));

app.MapRazorPages();

app.Run();

/// <summary>Exposed for integration tests (WebApplicationFactory).</summary>
public partial class Program;
