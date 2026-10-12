using System.Net;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.Infrastructure.Identity;
using ShutkiVorta.IntegrationTests.Infrastructure;
using static ShutkiVorta.IntegrationTests.Infrastructure.StaffTestHelper;

namespace ShutkiVorta.IntegrationTests;

/// <summary>
/// Who can open what in the admin area. Every test creates its own people (unique emails, per-person permissions) in the
/// shared app; role-level changes live in <see cref="StaffRoleTests"/>, which use apps of their own.
/// </summary>
[Collection("app")]
public sealed partial class StaffAccessTests(TestServers servers)
{
    private static readonly string[] ManagerNav =
        ["/admin/time-clock", "/admin", "/admin/orders", "/admin/recurring", "/admin/production", "/admin/menu", "/admin/inventory",
         "/admin/inquiries", "/admin/customers", "/admin/users", "/admin/emails"];

    private static readonly string[] EmployeeNav = ["/admin/time-clock", "/admin", "/admin/orders", "/admin/production", "/admin/menu", "/admin/inventory"];

    private static readonly string[] AllNav = [.. ManagerNav, "/admin/permissions", "/admin/settings"];

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task ManagersAndEmployees_SeeOnlyTheirModulesInTheNav(string provider)
    {
        var app = servers.Get(provider);
        var manager = await CreateStaffAsync(app, StaffRole.Manager);
        var employee = await CreateStaffAsync(app, StaffRole.Employee);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);

        Assert.Equal(ManagerNav, NavLinks(await (await SignInAsync(app, manager.Email)).GetStringAsync("/admin")));
        Assert.Equal(EmployeeNav, NavLinks(await (await SignInAsync(app, employee.Email)).GetStringAsync("/admin")));
        Assert.Equal(AllNav.OrderBy(l => l), NavLinks(await admin.GetStringAsync("/admin")).OrderBy(l => l));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task Employees_AreRefusedModulesTheyLack_WithA403(string provider)
    {
        var app = servers.Get(provider);
        var employee = await SignInAsync(app, (await CreateStaffAsync(app, StaffRole.Employee)).Email);

        foreach (var path in new[] { "/admin/customers", "/admin/settings", "/admin/settings/email", "/admin/inquiries", "/admin/emails", "/admin/recurring" })
        {
            AssertDenied(await employee.GetAsync(path));
        }

        var refused = await employee.GetAsync("/admin/settings");
        var page = WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync());
        Assert.Contains("Your account doesn't include this part of the admin", page);
        Assert.Contains("href=\"/admin/time-clock\"", page);

        Assert.Equal(HttpStatusCode.OK, (await employee.GetAsync("/admin/menu")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await employee.GetAsync("/admin/production")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task Employees_PostingAChangeTheyLack_Get403_AndNothingChanges(string provider)
    {
        var app = servers.Get(provider);
        var employee = await SignInAsync(app, (await CreateStaffAsync(app, StaffRole.Employee)).Email);
        var item = (await SendAsync(app, new GetMenuQuery()))[0];

        var menuPage = await employee.GetStringAsync("/admin/menu");
        Assert.DoesNotContain("handler=Availability", menuPage);
        Assert.DoesNotContain("handler=Delete", menuPage);
        Assert.DoesNotContain("New item", menuPage);

        AssertDenied(await PostFormAsync(employee, "/admin/menu?handler=Availability", menuPage,
            new Dictionary<string, string> { ["id"] = item.Id.ToString(), ["isAvailable"] = "false" }));
        AssertDenied(await PostFormAsync(employee, "/admin/menu?handler=Delete", menuPage,
            new Dictionary<string, string> { ["id"] = item.Id.ToString() }));

        var after = await SendAsync(app, new GetMenuItemBySlugQuery(item.Slug));
        Assert.NotNull(after);
        Assert.True(after.IsAvailable);

        // The editor opens read-only for viewers, and "new item" is refused.
        var editor = await employee.GetStringAsync($"/admin/menu/edit/{item.Id}");
        Assert.Contains("<fieldset class=\"read-only\" disabled=\"disabled\">", editor);
        Assert.DoesNotContain("Save item", editor);
        AssertDenied(await employee.GetAsync("/admin/menu/edit"));
        AssertDenied(await PostFormAsync(employee, $"/admin/menu/edit/{item.Id}", editor,
            new Dictionary<string, string> { ["Input.Name"] = "Renamed by an employee" }));
        Assert.Equal(item.Name, (await SendAsync(app, new GetMenuItemBySlugQuery(item.Slug)))!.Name);

        AssertDenied(await employee.GetAsync("/admin/inventory/edit"));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task SignedInCustomers_AreSentToAccessDenied_AndVisitorsToSignIn(string provider)
    {
        var app = servers.Get(provider);
        var email = $"customer-{Guid.NewGuid():N}@test.local";
        await CreateAccountAsync(app, email, Roles.Customer, emailConfirmed: true);

        var customer = CreateClient(app);
        var login = await LoginAsync(customer, email);
        Assert.Equal("/account/orders", login.Headers.Location!.OriginalString);

        AssertNotStaff(await customer.GetAsync("/admin"));
        AssertNotStaff(await customer.GetAsync("/admin/orders"));
        AssertSignInRequired(await CreateClient(app).GetAsync("/admin/menu"));
        Assert.DoesNotContain("title=\"Staff area\"", await customer.GetStringAsync("/"));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task AnUnconfirmedAccountWithAStaffRole_HasNoStaffAccess(string provider)
    {
        var app = servers.Get(provider);
        var email = $"unconfirmed-{Guid.NewGuid():N}@test.local";
        var userId = await CreateAccountAsync(app, email, role: null, emailConfirmed: false);
        using (var scope = app.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IStaffPermissionStore>().SetStaffRoleAsync(userId, StaffRole.Manager, "test-setup", "tests");
            Assert.True(result.Succeeded);
        }

        var client = CreateClient(app);
        var login = await LoginAsync(client, email);
        Assert.Equal("/account/orders", login.Headers.Location!.OriginalString);
        AssertNotStaff(await client.GetAsync("/admin"));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task ASignInFromBeforeTheAccountWasReset_LosesStaffAccessAtOnce(string provider)
    {
        // An admin taking an account over (or a password reset) rotates its stamp; Identity's own cookie check runs only
        // every few minutes, so the stamp is compared on every request: the old sign-in ends at once (not just its staff access).
        var app = servers.Get(provider);
        var manager = await CreateStaffAsync(app, StaffRole.Manager);
        var before = await SignInAsync(app, manager.Email);
        Assert.Equal(HttpStatusCode.OK, (await before.GetAsync("/admin/orders")).StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.UpdateSecurityStampAsync((await users.FindByIdAsync(manager.UserId))!)).Succeeded);
        }

        AssertSignInRequired(await before.GetAsync("/admin/orders"));
        AssertSignInRequired(await before.GetAsync("/account/manage"));
        var after = await SignInAsync(app, manager.Email);
        Assert.Equal(HttpStatusCode.OK, (await after.GetAsync("/admin/orders")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task AnEmployeesDashboard_ShowsNoMoney(string provider)
    {
        var app = servers.Get(provider);
        await PlaceOrderAsync(app); // so there is something the employee could otherwise see a price for
        var employee = await SignInAsync(app, (await CreateStaffAsync(app, StaffRole.Employee)).Email);

        var response = await employee.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Upcoming pickups", html);
        Assert.DoesNotContain("$", html);
        Assert.DoesNotContain("Online sales", html);

        var admin = await (await SignInAsync(app, AppFactory.AdminEmail)).GetStringAsync("/admin");
        Assert.Contains("$", admin);
        Assert.Contains("Online sales", admin);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task StaffWithoutTheDashboard_LandOnTheTimeClock(string provider)
    {
        var app = servers.Get(provider);
        var employee = await CreateStaffAsync(app, StaffRole.Employee, customPermissions: [Permissions.MenuView]);

        var client = CreateClient(app);
        var login = await LoginAsync(client, employee.Email);
        Assert.Equal("/admin/time-clock", login.Headers.Location!.OriginalString);

        var home = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, home.StatusCode);
        Assert.Equal("/admin/time-clock", home.Headers.Location!.OriginalString);

        var menu = await client.GetStringAsync("/admin/menu");
        Assert.Equal(["/admin/time-clock", "/admin/menu"], NavLinks(menu));
        Assert.Contains("href=\"/admin/time-clock\"", BrandLink().Match(menu).Value);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task SigningIn_SendsEachPersonToTheirLandingPage(string provider)
    {
        var app = servers.Get(provider);
        var manager = await CreateStaffAsync(app, StaffRole.Manager);
        var employee = await CreateStaffAsync(app, StaffRole.Employee);

        Assert.Equal("/admin", (await LoginAsync(CreateClient(app), AppFactory.AdminEmail)).Headers.Location!.OriginalString);
        Assert.Equal("/admin", (await LoginAsync(CreateClient(app), manager.Email)).Headers.Location!.OriginalString);
        Assert.Equal("/admin", (await LoginAsync(CreateClient(app), employee.Email)).Headers.Location!.OriginalString);
        Assert.Equal("/admin/orders", (await LoginAsync(CreateClient(app), manager.Email, returnUrl: "/admin/orders")).Headers.Location!.OriginalString);

        // Already signed in: the sign-in page forwards to the landing page too.
        var signedIn = await SignInAsync(app, manager.Email);
        var again = await signedIn.GetAsync("/account/login");
        Assert.Equal("/admin", again.Headers.Location!.OriginalString);

        // The public header links staff to the admin area (from the cookie's roles: display only).
        Assert.Contains("title=\"Staff area\"", await signedIn.GetStringAsync("/"));
        Assert.Contains("title=\"Admin panel\"", await (await SignInAsync(app, AppFactory.AdminEmail)).GetStringAsync("/"));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task CustomPermissions_ApplyOnTheNextRequest(string provider)
    {
        var app = servers.Get(provider);
        var employee = await CreateStaffAsync(app, StaffRole.Employee);
        var client = await SignInAsync(app, employee.Email);
        AssertDenied(await client.GetAsync("/admin/inquiries"));

        await SetCustomPermissionsAsync(app, employee.UserId, [Permissions.InquiriesView, Permissions.MenuView]);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/inquiries")).StatusCode);
        AssertDenied(await client.GetAsync("/admin/orders"));
        var home = await client.GetAsync("/admin");
        Assert.Equal("/admin/time-clock", home.Headers.Location!.OriginalString);

        await SetCustomPermissionsAsync(app, employee.UserId, null);
        AssertDenied(await client.GetAsync("/admin/inquiries"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/orders")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task TheTrackingLink_IsShownOnlyToStaffWhoMayChangeOrders(string provider)
    {
        var app = servers.Get(provider);
        var order = await PlaceOrderAsync(app);
        var viewer = await SignInAsync(app, (await CreateStaffAsync(app, StaffRole.Employee, [Permissions.OrdersView])).Email);
        var manager = await SignInAsync(app, (await CreateStaffAsync(app, StaffRole.Manager)).Email);

        var viewerPage = await viewer.GetStringAsync($"/admin/orders/{order.OrderNumber}");
        Assert.DoesNotContain(order.TrackingToken, viewerPage);
        Assert.DoesNotContain("handler=Status", viewerPage);
        Assert.DoesNotContain("handler=Notes", viewerPage);
        AssertDenied(await PostFormAsync(viewer, $"/admin/orders/{order.OrderNumber}?handler=Status", viewerPage,
            new Dictionary<string, string> { ["newStatus"] = nameof(OrderStatus.Confirmed) }));

        var managerPage = await manager.GetStringAsync($"/admin/orders/{order.OrderNumber}");
        Assert.Contains(order.TrackingToken, managerPage);
        Assert.Contains("handler=Status", managerPage);
    }

    [Fact]
    public async Task GeolocationIsAllowed_OnlyOnTheTimeClock()
    {
        var client = CreateClient(servers.Get("Sqlite"));

        Assert.Equal("camera=(), microphone=(), geolocation=(self)", PermissionsPolicy(await client.GetAsync("/admin/time-clock")));
        Assert.Equal("camera=(), microphone=(), geolocation=(self)", PermissionsPolicy(await client.GetAsync("/admin/time-clock/shifts/1")));
        Assert.Equal("camera=(), microphone=(), geolocation=()", PermissionsPolicy(await client.GetAsync("/")));
        Assert.Equal("camera=(), microphone=(), geolocation=()", PermissionsPolicy(await client.GetAsync("/admin")));
        Assert.Equal("camera=(), microphone=(), geolocation=()", PermissionsPolicy(await client.GetAsync("/admin/time-clockx")));
    }

    // ---- Helpers ----

    private static string PermissionsPolicy(HttpResponseMessage response) => string.Join(",", response.Headers.GetValues("Permissions-Policy"));

    private static List<string> NavLinks(string html) =>
        NavLink().Matches(AdminNav().Match(html).Value)
            .Select(m => m.Groups[1].Value)
            .Where(href => href.StartsWith("/admin", StringComparison.Ordinal))
            .ToList();

    private static async Task<T> SendAsync<T>(AppFactory app, IRequest<T> request)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    private static async Task<PlaceOrderResult> PlaceOrderAsync(AppFactory app)
    {
        var checkout = await SendAsync(app, new GetCheckoutOptionsQuery());
        var menu = await SendAsync(app, new GetMenuQuery());
        return await SendAsync(app, new PlaceOrderCommand
        {
            CustomerName = "Staff Test Customer",
            Email = $"order-{Guid.NewGuid():N}@test.local",
            Phone = "214-555-0199",
            Fulfillment = FulfillmentMethod.Pickup,
            ScheduledDate = checkout.Days[0].Date,
            ScheduledTime = checkout.Days[0].Slots[0],
            Lines = [new CartLineInput(menu[0].Id, 1m)],
        });
    }

    private static async Task<string> CreateAccountAsync(AppFactory app, string email, string? role, bool emailConfirmed)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, FullName = "Test Person", EmailConfirmed = emailConfirmed };
        Assert.True((await users.CreateAsync(user, AppFactory.AdminPassword)).Succeeded);
        if (role is not null)
        {
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        }

        return user.Id;
    }

    [GeneratedRegex("<ul class=\"admin-nav\">.*?</ul>", RegexOptions.Singleline)]
    private static partial Regex AdminNav();

    [GeneratedRegex("<a [^>]*href=\"([^\"]+)\"")]
    private static partial Regex NavLink();

    [GeneratedRegex("<a class=\"brand\"[^>]*>")]
    private static partial Regex BrandLink();
}
