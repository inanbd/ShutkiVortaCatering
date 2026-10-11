using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Infrastructure.Identity;

namespace ShutkiVorta.IntegrationTests.Infrastructure;

/// <summary>A staff member created for a test (password: <see cref="AppFactory.AdminPassword"/>).</summary>
public sealed record TestStaff(string UserId, string Email, string FullName, StaffRole Role);

/// <summary>
/// Creates staff members and signs them in, the same way in every test class.
/// Rules for tests in the shared "app" collection: create your own people with these helpers (unique emails) and give them
/// per-person custom permissions. Anything that changes a role's permissions, demotes or removes Admins, or checks whole
/// lists uses its own <c>await using var app = new AppFactory(provider)</c>. Never change admin@test.local.
/// </summary>
public static partial class StaffTestHelper
{
    public const string BaseAddress = "https://shutki.test";

    /// <summary>
    /// A confirmed account with a password, given <paramref name="role"/> through <see cref="IStaffPermissionStore"/> (as the Users
    /// page does), and, when <paramref name="customPermissions"/> is given, exactly those permissions instead of the role's.
    /// </summary>
    public static async Task<TestStaff> CreateStaffAsync(
        AppFactory app, StaffRole role, IEnumerable<string>? customPermissions = null, string? email = null)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        email ??= $"{role.RoleName().ToLowerInvariant()}-{tag}@test.local";
        var fullName = $"{role.RoleName()} {tag}";

        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, FullName = fullName, CreatedAtUtc = DateTime.UtcNow };
        var created = await users.CreateAsync(user, AppFactory.AdminPassword);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));

        var store = scope.ServiceProvider.GetRequiredService<IStaffPermissionStore>();
        var assigned = await store.SetStaffRoleAsync(user.Id, role, actorUserId: "test-setup", changedBy: "tests");
        Assert.True(assigned.Succeeded, string.Join("; ", assigned.Errors));

        if (customPermissions is not null)
        {
            await SetCustomPermissionsAsync(app, user.Id, customPermissions);
        }

        return new TestStaff(user.Id, email, fullName, role);
    }

    /// <summary>Gives the person exactly these permissions (null: follow their role again).</summary>
    public static async Task SetCustomPermissionsAsync(AppFactory app, string userId, IEnumerable<string>? permissions)
    {
        using var scope = app.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IStaffPermissionStore>();
        var current = await store.GetUserPermissionsAsync(userId);
        var result = await store.SetUserPermissionsAsync(userId, permissions?.ToHashSet(), current.Version, "tests");
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    /// <summary>A client that does not follow redirects.</summary>
    public static HttpClient CreateClient(AppFactory app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri(BaseAddress) });

    /// <summary>Signs in (asserting it worked) and returns the signed-in client, which does not follow redirects.</summary>
    public static async Task<HttpClient> SignInAsync(AppFactory app, string email, string? password = null)
    {
        var client = CreateClient(app);
        var response = await LoginAsync(client, email, password);
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, $"Signing in as {email} failed ({(int)response.StatusCode}).");
        return client;
    }

    /// <summary>Posts the sign-in form; a successful sign-in is a 302 to the person's landing page.</summary>
    public static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string? password = null, string? returnUrl = null)
    {
        var url = returnUrl is null ? "/account/login" : $"/account/login?returnUrl={Uri.EscapeDataString(returnUrl)}";
        var login = await client.GetStringAsync(url);
        return await PostFormAsync(client, url, login, new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password ?? AppFactory.AdminPassword,
        });
    }

    public static string AntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenInput().Match(html);
        Assert.True(match.Success, "The page has no antiforgery token.");
        return match.Groups[1].Value;
    }

    /// <summary>Posts a form to <paramref name="url"/> with the antiforgery token of <paramref name="pageHtml"/> (the page the form is on).</summary>
    public static Task<HttpResponseMessage> PostFormAsync(HttpClient client, string url, string pageHtml, IDictionary<string, string> fields)
    {
        var form = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = AntiforgeryToken(pageHtml) };
        return client.PostAsync(url, new FormUrlEncodedContent(form));
    }

    /// <summary>
    /// The refusal a signed-in staff member gets for a page, handler or request they lack: a real 403, rendered by the error page.
    /// (Signed-in people without staff access are redirected to the access-denied page instead, and anonymous visitors to
    /// sign in: see <see cref="AssertNotStaff"/> and <see cref="AssertSignInRequired"/>.)
    /// </summary>
    public static void AssertDenied(HttpResponseMessage response) =>
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 403 for {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}, got {(int)response.StatusCode} {response.Headers.Location}.");

    /// <summary>A signed-in person without staff access asking for /admin: redirected to the access-denied page.</summary>
    public static void AssertNotStaff(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account/access-denied", response.Headers.Location!.PathAndQuery(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An anonymous visitor asking for /admin: redirected to sign in.</summary>
    public static void AssertSignInRequired(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account/login", response.Headers.Location!.PathAndQuery(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Waits for an email to <paramref name="to"/> in the pickup folder and returns the NEWEST one's To: header line followed
    /// by its HTML body. Pass <paramref name="sinceUtc"/> (taken before the action) to ignore emails written earlier, e.g.
    /// the first invitation when testing a resend.
    /// </summary>
    public static async Task<string> WaitForEmailAsync(AppFactory app, string to, TimeSpan timeout, DateTime? sinceUtc = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            // Each email is saved as .html (body) plus .eml (with headers).
            var mail = Directory.Exists(app.MailDirectory)
                ? Directory.GetFiles(app.MailDirectory, "*.html")
                    .Select(html => (Header: ToHeader(Path.ChangeExtension(html, ".eml")), Html: html, WrittenUtc: File.GetLastWriteTimeUtc(html)))
                    .Where(m => m.Header.Contains(to, StringComparison.OrdinalIgnoreCase) && (sinceUtc is null || m.WrittenUtc >= sinceUtc))
                    .OrderByDescending(m => m.WrittenUtc)
                    .Select(m => m.Header + "\n" + File.ReadAllText(m.Html))
                    .FirstOrDefault()
                : null;
            if (mail is not null)
            {
                return mail;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new Xunit.Sdk.XunitException($"No email to {to} arrived in {app.MailDirectory} within {timeout}.");
            }

            await Task.Delay(200);
        }
    }

    private static string PathAndQuery(this Uri uri) => uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;

    private static string ToHeader(string emlPath) =>
        File.Exists(emlPath) ? File.ReadLines(emlPath).FirstOrDefault(l => l.StartsWith("To:", StringComparison.OrdinalIgnoreCase)) ?? string.Empty : string.Empty;

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryTokenInput();
}
