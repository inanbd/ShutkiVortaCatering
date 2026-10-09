using System.Data.Common;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Application.Features.Wholesale;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.Domain.Wholesale;
using ShutkiVorta.Infrastructure.Identity;
using ShutkiVorta.Infrastructure.Persistence;
using ShutkiVorta.Infrastructure.Settings;
using ShutkiVorta.IntegrationTests.Infrastructure;

namespace ShutkiVorta.IntegrationTests;

/// <summary>
/// Business settings live in the database and are edited in Admin → Settings. Each test runs its own copy of the site,
/// because the settings it changes would affect the shared one.
/// </summary>
public sealed partial class SettingsTests
{
    public static TheoryData<string> Providers => TestServers.Providers;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Admin_ChangesSettingsInTheBrowser_AndTheSiteUsesThemAtOnce(string provider)
    {
        await using var app = new AppFactory(provider);
        var client = await AdminClientAsync(app);

        var overview = await client.GetStringAsync("/admin/settings");
        foreach (var slug in new[] { "business", "ordering", "restaurants", "email", "website", "accounts", "spam-protection" })
        {
            Assert.Contains($"href=\"/admin/settings/{slug}\"", overview);
        }

        // Ordering: sales tax as a percentage, closed days, and a shorter delivery ZIP list than the built-in one.
        var form = SettingsForm.Read(await client.GetStringAsync("/admin/settings/ordering"));
        Assert.Equal("8.25", form.Get("Ordering:TaxRate"));
        form.Set("Ordering:TaxRate", "7.5");
        form.Set("Ordering:ClosedDays", "Sunday", "Monday");
        form.Set("Ordering:DeliveryZipPrefixes", "750\n 751 \n\n");
        await SaveAsync(client, "ordering", form);

        var ordering = app.Services.GetRequiredService<IOptionsMonitor<OrderingOptions>>().CurrentValue;
        Assert.Equal(0.075m, ordering.TaxRate);
        Assert.Equal([DayOfWeek.Sunday, DayOfWeek.Monday], ordering.ClosedDays.Order());
        Assert.Equal(["750", "751"], ordering.DeliveryZipPrefixes);
        using (var scope = app.Services.CreateScope())
        {
            Assert.Equal(0.075m, scope.ServiceProvider.GetRequiredService<IOptions<OrderingOptions>>().Value.TaxRate);
        }

        // Email: the server configuration names an admin recipient; removing it here wins (the database is the only source).
        form = SettingsForm.Read(await client.GetStringAsync("/admin/settings/email"));
        Assert.Equal("kitchen@test.local", form.Get("Email:AdminRecipients"));
        form.Set("Email:AdminRecipients", string.Empty);
        form.Set("Email:FromName", "Shutki Vorta Kitchen");
        await SaveAsync(client, "email", form);

        var email = app.Services.GetRequiredService<IOptionsMonitor<EmailOptions>>().CurrentValue;
        Assert.Empty(email.AdminRecipients);
        Assert.Equal("Shutki Vorta Kitchen", email.FromName);
        Assert.Equal(app.MailDirectory, email.PickupDirectory); // still the server's folder, not editable here

        var saved = await client.GetStringAsync("/admin/settings/ordering?saved=1");
        Assert.Contains("Settings saved.", saved);
        Assert.Contains("Recent changes", saved);
        Assert.Contains("7.5%", saved);
        Assert.Contains(AppFactory.AdminEmail, saved);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task SmtpPassword_IsStoredEncrypted_NeverShown_AndMustBeRetypedWhenTheServerChanges(string provider)
    {
        const string password = "Pa55-w0rd!kitchen";
        await using var app = new AppFactory(provider);
        var client = await AdminClientAsync(app);

        var page = await client.GetStringAsync("/admin/settings/email");
        Assert.Contains("autocomplete=\"new-password\"", page);
        var form = SettingsForm.Read(page);
        form.Set("Email:Smtp:Host", "smtp.shutki.test");
        form.Set("Email:Smtp:UserName", "kitchen");
        form.Set("Email:Smtp:Password", password);
        await SaveAsync(client, "email", form);

        var monitor = app.Services.GetRequiredService<IOptionsMonitor<EmailOptions>>();
        Assert.Equal(password, monitor.CurrentValue.Smtp.Password);

        var stored = await ScalarAsync(app, "SELECT Value FROM AppSettings WHERE [Key] = 'Email:Smtp:Password'");
        Assert.StartsWith("enc:v1:", stored);
        Assert.DoesNotContain(password, stored);

        page = await client.GetStringAsync("/admin/settings/email");
        Assert.DoesNotContain(password, page);
        Assert.Contains("Leave blank to keep the saved one", page);
        Assert.Contains("(changed)", page); // in the history, instead of the value

        // Pointing the saved password at another server needs the password again.
        form = SettingsForm.Read(page);
        Assert.Equal(string.Empty, form.Get("Email:Smtp:Password"));
        form.Set("Email:Smtp:Host", "smtp.elsewhere.test");
        var rejected = await PostAsync(client, "email", form);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var body = WebUtility.HtmlDecode(await rejected.Content.ReadAsStringAsync());
        Assert.Contains("please enter the SMTP password again", body);
        Assert.Contains("Nothing was saved yet", body);
        Assert.Equal("smtp.shutki.test", monitor.CurrentValue.Smtp.Host);

        // Saving other email settings keeps the password.
        form = SettingsForm.Read(await client.GetStringAsync("/admin/settings/email"));
        form.Set("Email:FromName", "Kitchen");
        await SaveAsync(client, "email", form);
        Assert.Equal(password, monitor.CurrentValue.Smtp.Password);

        // Removing it.
        form = SettingsForm.Read(await client.GetStringAsync("/admin/settings/email"));
        form.Set("clear:Email:Smtp:Password", "true");
        await SaveAsync(client, "email", form);
        Assert.Null(monitor.CurrentValue.Smtp.Password);
        Assert.Null(await ScalarAsync(app, "SELECT Value FROM AppSettings WHERE [Key] = 'Email:Smtp:Password'"));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task TwoAdminsEditingTheSamePage_TheSecondIsAskedToReload(string provider)
    {
        await using var app = new AppFactory(provider);
        var client = await AdminClientAsync(app);

        var first = SettingsForm.Read(await client.GetStringAsync("/admin/settings/business"));
        var second = SettingsForm.Read(await client.GetStringAsync("/admin/settings/business"));

        first.Set("Business:Phone", "(469) 555-0101");
        await SaveAsync(client, "business", first);

        second.Set("Business:Phone", "(469) 555-0202");
        var response = await PostAsync(client, "business", second);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("changed by someone else", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal("(469) 555-0101", app.Services.GetRequiredService<IOptionsMonitor<BusinessOptions>>().CurrentValue.Phone);
    }

    [Fact]
    public async Task AStoredValueThatCannotBeUsed_IsIgnored_AndReportedUntilAnAdminFixesIt()
    {
        await using var app = new AppFactory("Sqlite");
        var client = await AdminClientAsync(app);

        await ExecuteAsync(app, "UPDATE AppSettings SET Value = 'a lot' WHERE [Key] = 'Ordering:MaxDaysInAdvance'");
        await ExecuteAsync(app, "UPDATE AppSettings SET Value = '12' WHERE [Key] = 'Ordering:MinimumLeadTimeHours'");
        await app.Services.GetRequiredService<SettingsConfiguration>().Provider.ReloadAsync();

        var ordering = app.Services.GetRequiredService<IOptionsMonitor<OrderingOptions>>().CurrentValue;
        Assert.Equal(new OrderingOptions().MaxDaysInAdvance, ordering.MaxDaysInAdvance); // the default
        Assert.Equal(12, ordering.MinimumLeadTimeHours); // the rest of the section still applies
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/menu")).StatusCode);

        var overview = WebUtility.HtmlDecode(await client.GetStringAsync("/admin/settings"));
        Assert.Contains("Some saved settings could not be used", overview);
        Assert.Contains("Ordering:MaxDaysInAdvance", overview);

        var form = SettingsForm.Read(await client.GetStringAsync("/admin/settings/ordering"));
        form.Set("Ordering:MaxDaysInAdvance", "45");
        await SaveAsync(client, "ordering", form);

        Assert.Equal(45, app.Services.GetRequiredService<IOptionsMonitor<OrderingOptions>>().CurrentValue.MaxDaysInAdvance);
        Assert.DoesNotContain("could not be used", WebUtility.HtmlDecode(await client.GetStringAsync("/admin/settings")));
    }

    [Fact]
    public async Task RequiringEmailConfirmation_AppliesAtOnce_ButNeverLocksOutAdmins()
    {
        await using var app = new AppFactory("Sqlite");
        var client = await AdminClientAsync(app);

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await CreateUserAsync(users, "new-admin@test.local", Roles.Admin);
            await CreateUserAsync(users, "new-customer@test.local", Roles.Customer);
        }

        var form = SettingsForm.Read(await client.GetStringAsync("/admin/settings/accounts"));
        form.Set("Identity:RequireConfirmedEmail", "true");
        await SaveAsync(client, "accounts", form);

        Assert.True(await SignInAsync(app, "new-admin@test.local"));
        Assert.False(await SignInAsync(app, "new-customer@test.local"));

        form = SettingsForm.Read(await client.GetStringAsync("/admin/settings/accounts"));
        form.Remove("Identity:RequireConfirmedEmail"); // an unticked switch is not posted
        await SaveAsync(client, "accounts", form);

        Assert.True(await SignInAsync(app, "new-customer@test.local"));
    }

    [Fact]
    public async Task ServerConfiguration_SeedsTheDatabaseOnce_IsReportedWhenItDiffers_AndCanBeReimportedOnPurpose()
    {
        var directory = AppFactory.NewDirectory();
        try
        {
            // First start: the configured values are copied into the database.
            await using (var app = new AppFactory("Sqlite", directory: directory))
            {
                Assert.Equal("https://shutki.test", Site(app).BaseUrl);
                Assert.Equal(1, await CountAsync(app, "SELECT COUNT(*) FROM AppSettingsSections WHERE Section = 'Site'"));
            }

            // Later starts ignore configuration that differs from what admins saved, and say so.
            var changed = new Dictionary<string, string?> { ["Site:BaseUrl"] = "https://other.test" };
            await using (var app = new AppFactory("Sqlite", changed, directory))
            {
                Assert.Equal("https://shutki.test", Site(app).BaseUrl);
                var overview = WebUtility.HtmlDecode(await (await AdminClientAsync(app)).GetStringAsync("/admin/settings"));
                Assert.Contains("Ignored server configuration", overview);
                Assert.Contains("Site:BaseUrl", overview);
            }

            // The recovery switch copies them in, once.
            var reimport = new Dictionary<string, string?>(changed) { ["Settings:ReimportFromConfiguration"] = "true" };
            await using (var app = new AppFactory("Sqlite", reimport, directory))
            {
                Assert.Equal("https://other.test", Site(app).BaseUrl);

                var client = await AdminClientAsync(app);
                var form = SettingsForm.Read(await client.GetStringAsync("/admin/settings/website"));
                form.Set("Site:BaseUrl", "https://www.shutkivorta.test");
                await SaveAsync(client, "website", form);
            }

            await using (var app = new AppFactory("Sqlite", reimport, directory))
            {
                Assert.Equal("https://www.shutkivorta.test", Site(app).BaseUrl); // not re-imported again
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ASectionMissingOnALiveSite_IsFilledWithDefaults_AndFlaggedForReviewUntilSaved()
    {
        var directory = AppFactory.NewDirectory();
        try
        {
            // A site that already takes orders, upgraded from a version whose Business settings were only in appsettings.json.
            await using (var app = new AppFactory("Sqlite", directory: directory))
            {
                await AddStandingOrderAsync(app);
                await ExecuteAsync(app, "DELETE FROM AppSettings WHERE [Key] LIKE 'Business:%'");
                await ExecuteAsync(app, "DELETE FROM AppSettingsSections WHERE Section = 'Business'");
            }

            await using (var app = new AppFactory("Sqlite", directory: directory))
            {
                var client = await AdminClientAsync(app);
                var overview = WebUtility.HtmlDecode(await client.GetStringAsync("/admin/settings"));
                Assert.Contains("Please check these settings", overview);
                Assert.Contains("href=\"/admin/settings/business\">Business details</a>", overview);

                var page = WebUtility.HtmlDecode(await client.GetStringAsync("/admin/settings/business"));
                Assert.Contains("Please check these settings.", page);
                Assert.DoesNotContain("Please check these settings.", WebUtility.HtmlDecode(await client.GetStringAsync("/admin/settings/ordering")));

                await SaveAsync(client, "business", SettingsForm.Read(page));
                Assert.DoesNotContain("Please check these settings", WebUtility.HtmlDecode(await client.GetStringAsync("/admin/settings")));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SettingsPages_AreForAdminsOnly()
    {
        await using var app = new AppFactory("Sqlite");
        var anonymous = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://shutki.test") });
        var response = await anonymous.GetAsync("/admin/settings/email");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.ToString());

        var admin = await AdminClientAsync(app);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/admin/settings/database")).StatusCode);
    }

    // ---- Helpers --------------------------------------------------------------------------------------------------------

    private static SiteOptions Site(AppFactory app) => app.Services.GetRequiredService<IOptionsMonitor<SiteOptions>>().CurrentValue;

    private static async Task<HttpClient> AdminClientAsync(AppFactory app)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://shutki.test") });
        Assert.True(await SignInAsync(client, AppFactory.AdminEmail, AppFactory.AdminPassword));
        return client;
    }

    private static async Task<bool> SignInAsync(AppFactory app, string email) =>
        await SignInAsync(
            app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://shutki.test") }),
            email,
            AppFactory.AdminPassword);

    private static async Task<bool> SignInAsync(HttpClient client, string email, string password)
    {
        var login = await client.GetStringAsync("/account/login");
        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = AntiforgeryToken().Match(login).Groups[1].Value,
        }));
        return response.StatusCode == HttpStatusCode.Redirect;
    }

    private static async Task CreateUserAsync(UserManager<ApplicationUser> users, string email, string role)
    {
        var user = new ApplicationUser { UserName = email, Email = email, FullName = "Test Person", EmailConfirmed = false };
        Assert.True((await users.CreateAsync(user, AppFactory.AdminPassword)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string slug, SettingsForm form) =>
        client.PostAsync($"/admin/settings/{slug}", new FormUrlEncodedContent(form.Fields));

    private static async Task SaveAsync(HttpClient client, string slug, SettingsForm form)
    {
        var response = await PostAsync(client, slug, form);
        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            var errors = ErrorLine().Matches(WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync())).Select(m => m.Groups[1].Value);
            Assert.Fail($"Saving {slug} failed ({(int)response.StatusCode}): {string.Join(" | ", errors)}");
        }

        Assert.Equal($"/admin/settings/{slug}?saved=1", response.Headers.Location!.OriginalString);
    }

    private static async Task AddStandingOrderAsync(AppFactory app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var item = (await services.GetRequiredService<IMenuItemRepository>().GetBySlugAsync("shim-vorta"))!;
        var start = DateOnly.FromDateTime(clock.BusinessNow).AddDays(2);
        var order = StandingOrder.Submit(
            $"RO-SET-{Guid.NewGuid():N}"[..16], null, "Rupchanda Grill", new CustomerContact("Rina Das", "rina@rupchanda.test", "(214) 555-0177"), null,
            FulfillmentMethod.Pickup, null, WeekDays.EveryDay, new TimeOnly(11, 0), start, null, null,
            [new StandingOrderLineRequest(item.Id, item.Name, item.BengaliName, item.Unit, 15m, 10m)], 0m, new WholesaleRules(5m, 1m, 100m), clock.UtcNow);
        await services.GetRequiredService<IStandingOrderRepository>().AddAsync(order);
    }

    private static async Task<DbCommand> CommandAsync(AppFactory app, string sql)
    {
        var connection = await app.Services.GetRequiredService<IDbConnectionFactory>().OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Disposed += (_, _) => connection.Dispose();
        return command;
    }

    private static async Task ExecuteAsync(AppFactory app, string sql)
    {
        await using var command = await CommandAsync(app, sql);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ScalarAsync(AppFactory app, string sql)
    {
        await using var command = await CommandAsync(app, sql);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task<int> CountAsync(AppFactory app, string sql)
    {
        await using var command = await CommandAsync(app, sql);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    [GeneratedRegex("<span class=\"error-line\">([^<]*)</span>")]
    private static partial Regex ErrorLine();

    /// <summary>The settings editor form as a browser would submit it (inputs named by setting key).</summary>
    private sealed partial class SettingsForm
    {
        private readonly List<KeyValuePair<string, string>> _fields = [];

        public IReadOnlyList<KeyValuePair<string, string>> Fields => _fields;

        public static SettingsForm Read(string html)
        {
            var start = html.IndexOf("data-settings-form", StringComparison.Ordinal);
            Assert.True(start > 0, "The settings form was not found.");
            start = html.LastIndexOf("<form", start, StringComparison.Ordinal);
            var markup = html[start..(html.IndexOf("</form>", start, StringComparison.Ordinal) + 7)];
            var form = new SettingsForm();

            foreach (Match input in InputTag().Matches(markup))
            {
                var attributes = Attributes(input.Value);
                if (!attributes.TryGetValue("name", out var name) || name is null
                    || (attributes.GetValueOrDefault("type") is "checkbox" or "radio" && !attributes.ContainsKey("checked")))
                {
                    continue;
                }

                form._fields.Add(new(name, attributes.GetValueOrDefault("value") ?? string.Empty));
            }

            foreach (Match textarea in TextareaTag().Matches(markup))
            {
                form._fields.Add(new(Attributes(textarea.Groups[1].Value)["name"]!, WebUtility.HtmlDecode(textarea.Groups[2].Value)));
            }

            foreach (Match select in SelectTag().Matches(markup))
            {
                var options = OptionTag().Matches(select.Groups[2].Value).Select(o => Attributes(o.Value)).ToList();
                var chosen = options.FirstOrDefault(o => o.ContainsKey("selected")) ?? options.FirstOrDefault();
                form._fields.Add(new(Attributes(select.Groups[1].Value)["name"]!, chosen?.GetValueOrDefault("value") ?? string.Empty));
            }

            return form;
        }

        public string? Get(string name)
        {
            var values = _fields.Where(f => f.Key == name).Select(f => f.Value).ToList();
            return values.Count == 0 ? null : string.Join("\n", values);
        }

        public void Set(string name, params string[] values)
        {
            Remove(name);
            _fields.AddRange(values.Select(v => new KeyValuePair<string, string>(name, v)));
        }

        public void Remove(string name) => _fields.RemoveAll(f => f.Key == name);

        private static Dictionary<string, string?> Attributes(string tag) =>
            Attribute().Matches(tag)
                .Where(m => m.Groups[1].Value is not ("input" or "textarea" or "select" or "option"))
                .GroupBy(m => m.Groups[1].Value)
                .ToDictionary(g => g.Key, g => g.First().Groups[2].Success ? WebUtility.HtmlDecode(g.First().Groups[2].Value) : null);

        [GeneratedRegex("<input\\b[^>]*>", RegexOptions.Singleline)]
        private static partial Regex InputTag();

        [GeneratedRegex("<textarea\\b([^>]*)>(.*?)</textarea>", RegexOptions.Singleline)]
        private static partial Regex TextareaTag();

        [GeneratedRegex("<select\\b([^>]*)>(.*?)</select>", RegexOptions.Singleline)]
        private static partial Regex SelectTag();

        [GeneratedRegex("<option\\b[^>]*>", RegexOptions.Singleline)]
        private static partial Regex OptionTag();

        [GeneratedRegex("([a-zA-Z_:][-a-zA-Z0-9_:.]*)(?:=\"([^\"]*)\")?")]
        private static partial Regex Attribute();
    }
}
