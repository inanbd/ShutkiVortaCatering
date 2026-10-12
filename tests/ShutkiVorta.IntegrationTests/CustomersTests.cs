using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.IntegrationTests.Infrastructure;
using ShutkiVorta.Web.Infrastructure.Security;
using ShutkiVorta.Web.Pages.Admin.Customers;
using static ShutkiVorta.IntegrationTests.Infrastructure.CustomerTestData;
using static ShutkiVorta.IntegrationTests.Infrastructure.StaffTestHelper;

namespace ShutkiVorta.IntegrationTests;

/// <summary>
/// The customer pages end to end: who may open them, which accounts and orders have a page, and the links and sections
/// that depend on the viewer's other permissions. Every test uses its own people (unique emails) in the shared app and finds
/// them by searching, so other tests' customers do not matter.
/// </summary>
[Collection("app")]
public sealed partial class CustomersTests(TestServers servers)
{
    private const string Customers = "/admin/customers";

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task AManager_OpensTheList_AndTheAccountAndGuestPages(string provider)
    {
        var app = servers.Get(provider);
        var tag = Tag();
        var email = $"rina-{tag}@test.local";
        var account = await CreateAccountAsync(app, $"Rina {tag}", email, phone: "214-555-0111");
        var own = await AddOrderAsync(app, new TestOrder(email, 42.50m, DateTime.UtcNow.AddDays(-3)) { CustomerId = account, Name = $"Rina {tag}" });
        var guestOrder = await AddOrderAsync(app, new TestOrder($"guest-{tag}@test.local", 18m, DateTime.UtcNow.AddDays(-2)) { Name = $"Guest {tag}" });
        var manager = await SignInAsync(app, (await CreateStaffAsync(app, StaffRole.Manager)).Email);

        // The list: both, with their pages' links (an account id and an order number: no email in any address).
        var list = await PageAsync(manager, $"{Customers}?q={tag}");
        Assert.Contains($"Rina {tag}", list);
        Assert.Contains($"Guest {tag}", list);
        Assert.Contains($"href=\"{Customers}/{account}\"", list);
        Assert.Contains($"href=\"{Customers}/guest/{guestOrder}\"", list);
        Assert.Contains("$42.50", list);
        Assert.Contains("payments are not tracked", list);
        Assert.DoesNotMatch(LinkWithEmail(), list);
        Assert.DoesNotContain("Make admin", list);

        // The account's page: contact, figures and the order, linked (Managers may see orders by default).
        var page = await PageAsync(manager, $"{Customers}/{account}");
        Assert.Contains($"<h1>Rina {tag}</h1>", page);
        Assert.Contains("214-555-0111", page);
        Assert.Contains("Email confirmed", page);
        Assert.Contains($"href=\"/admin/orders/{own}\"", page);
        Assert.Contains("$42.50", page);

        // The guest's page, found by their order number.
        var guest = await PageAsync(manager, $"{Customers}/guest/{guestOrder}");
        Assert.Contains($"<h1>Guest {tag}</h1>", guest);
        Assert.Contains("Ordered without an account", guest);
        Assert.Contains($"guest-{tag}@test.local", guest);
        Assert.Contains("$18.00", guest);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task Employees_AreRefused_AndOnlyStaffCanAsk(string provider)
    {
        var app = servers.Get(provider);
        var tag = Tag();
        var email = $"sami-{tag}@test.local";
        var account = await CreateAccountAsync(app, $"Sami {tag}", email);
        var order = await AddOrderAsync(app, new TestOrder($"walkin-{tag}@test.local", 9m, DateTime.UtcNow.AddDays(-1)));
        var employee = await SignInAsync(app, (await CreateStaffAsync(app, StaffRole.Employee)).Email);
        var customer = await SignInAsync(app, email);
        var visitor = CreateClient(app);

        foreach (var path in new[] { Customers, $"{Customers}/{account}", $"{Customers}/guest/{order}" })
        {
            AssertDenied(await employee.GetAsync(path));
            AssertNotStaff(await customer.GetAsync(path));
            AssertSignInRequired(await visitor.GetAsync(path));
        }
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task OnlyCustomersHavePages_AndAnOrderOfAnAccount_LeadsToTheAccount(string provider)
    {
        var app = servers.Get(provider);
        var tag = Tag();
        var viewer = await SignInAsync(app, (await CreateStaffAsync(app, StaffRole.Employee, [Permissions.CustomersView])).Email);

        // Staff-only accounts, unknown ids and unknown order numbers have no page; "guest" is not an account id.
        var staffOnly = await CreateStaffAsync(app, StaffRole.Manager);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"{Customers}/{staffOnly.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"{Customers}/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"{Customers}/guest")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"{Customers}/guest/SV-NO-SUCH-{tag}")).StatusCode);
        Assert.Contains("Nobody matches your search.", await PageAsync(viewer, $"{Customers}?q={staffOnly.Email}"));

        // A guest order placed with a confirmed account's email, and an order placed while signed in, belong to the account.
        var email = $"tania-{tag}@test.local";
        var account = await CreateAccountAsync(app, $"Tania {tag}", email);
        var guestOrder = await AddOrderAsync(app, new TestOrder(email.ToUpperInvariant(), 30m, DateTime.UtcNow.AddDays(-4)));
        var ownOrder = await AddOrderAsync(app, new TestOrder(email, 20m, DateTime.UtcNow.AddDays(-3)) { CustomerId = account });
        foreach (var order in new[] { guestOrder, ownOrder })
        {
            var response = await viewer.GetAsync($"{Customers}/guest/{order}");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal($"{Customers}/{account}", response.Headers.Location!.OriginalString);
        }

        Assert.Contains("$50.00", await PageAsync(viewer, $"{Customers}/{account}"));

        // An account whose email is not confirmed does not take the guest orders placed with it: they keep a guest page,
        // which points to the account.
        var unconfirmedEmail = $"uma-{tag}@test.local";
        var unconfirmed = await CreateAccountAsync(app, $"Uma {tag}", unconfirmedEmail, confirmed: false);
        var umaOrder = await AddOrderAsync(app, new TestOrder(unconfirmedEmail, 11m, DateTime.UtcNow.AddDays(-2)) { Name = $"Uma Guest {tag}" });
        var umaGuest = await PageAsync(viewer, $"{Customers}/guest/{umaOrder}");
        Assert.Contains($"<h1>Uma Guest {tag}</h1>", umaGuest);
        Assert.Contains("hasn't confirmed it", umaGuest);
        Assert.Contains($"href=\"{Customers}/{unconfirmed}\"", umaGuest);
        var umaAccount = await PageAsync(viewer, $"{Customers}/{unconfirmed}");
        Assert.Contains("Email not confirmed", umaAccount);
        Assert.Contains("No orders yet.", umaAccount);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task LinksAndSections_FollowTheViewersOtherPermissions(string provider)
    {
        var app = servers.Get(provider);
        var tag = Tag();

        // A restaurant that is also on the staff, with an order, a standing order and an inquiry.
        var person = await CreateStaffAsync(app, StaffRole.Employee, email: $"nadia-{tag}@test.local");
        var order = await AddOrderAsync(app, new TestOrder(person.Email, 64m, DateTime.UtcNow.AddDays(-5)) { CustomerId = person.UserId });
        var standing = await AddStandingOrderAsync(app, person.UserId, $"Nadia's Grill {tag}", person.Email);
        await AddInquiryAsync(app, person.FullName, person.Email.ToUpperInvariant(), $"Catering for 80 people {tag}", DateTime.UtcNow.AddDays(-6));
        var page = $"{Customers}/{person.UserId}";

        var onlyCustomers = await SignInAsync(app, (await CreateStaffAsync(app, StaffRole.Employee, [Permissions.CustomersView])).Email);
        var narrow = await PageAsync(onlyCustomers, page);
        Assert.Contains(order, narrow);
        Assert.DoesNotContain($"href=\"/admin/orders/{order}\"", narrow);
        Assert.Contains($"Nadia's Grill {tag}", narrow);
        Assert.DoesNotContain($"href=\"/admin/recurring/{standing}\"", narrow);
        Assert.Contains("Also staff", narrow);
        Assert.DoesNotContain($"href=\"/admin/users/{person.UserId}\"", narrow);
        Assert.DoesNotContain($"Catering for 80 people {tag}", narrow);
        Assert.DoesNotContain("All inquiries", narrow);

        var wide = await SignInAsync(app, (await CreateStaffAsync(app, StaffRole.Employee,
            [Permissions.CustomersView, Permissions.OrdersView, Permissions.RestaurantsView, Permissions.InquiriesView, Permissions.StaffView])).Email);
        var full = await PageAsync(wide, page);
        Assert.Contains($"href=\"/admin/orders/{order}\"", full);
        Assert.Contains($"href=\"/admin/recurring/{standing}\"", full);
        Assert.Contains($"href=\"/admin/users/{person.UserId}\"", full);
        Assert.Contains($"Catering for 80 people {tag}", full);

        // Restaurants are a type of their own in the list, with the restaurant's name.
        var restaurants = await PageAsync(wide, $"{Customers}?type=restaurant&q={tag}");
        Assert.Contains($"Nadia's Grill {tag}", restaurants);
        Assert.Contains("pill-restaurant", restaurants);
        Assert.DoesNotContain(person.Email, await PageAsync(wide, $"{Customers}?type=guest&q={tag}"));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task TheOldMakeAdminAction_IsGone(string provider)
    {
        var app = servers.Get(provider);
        var manager = await CreateStaffAsync(app, StaffRole.Manager);
        var client = await SignInAsync(app, manager.Email);
        var page = await client.GetStringAsync(Customers);

        var response = await PostFormAsync(client, $"{Customers}?handler=Admin", page,
            new Dictionary<string, string> { ["userId"] = manager.UserId, ["isAdmin"] = "true" });

        // There is no such handler any more: the list is simply shown again, and nothing changed.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Make admin", await response.Content.ReadAsStringAsync());
        using var scope = app.Services.CreateScope();
        var access = await scope.ServiceProvider.GetRequiredService<IStaffAccessService>().GetAsync(manager.UserId);
        Assert.Equal(StaffRole.Manager, access.Role);
        Assert.False(access.IsAdmin);
    }

    /// <summary>The customer pages: customers.view each, and nothing but GET (they change nothing).</summary>
    [Fact]
    public void TheCustomerPages_HaveTheirAgreedAccess_AndNoActions()
    {
        var pages = new RazorCompiledItemLoader().LoadItems(typeof(Program).Assembly)
            .Where(i => i.Kind == "mvc.1.0.razor-page" && i.Identifier.StartsWith("/Pages/Admin/Customers/", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Identifier)
            .Order(StringComparer.Ordinal);
        Assert.Equal(["/Pages/Admin/Customers/Customer.cshtml", "/Pages/Admin/Customers/Guest.cshtml", "/Pages/Admin/Customers/Index.cshtml"], pages);

        foreach (var model in new[] { typeof(IndexModel), typeof(CustomerModel), typeof(GuestModel) })
        {
            var rule = Assert.IsType<RequirePermissionAttribute>(Assert.Single(PermissionPageFilter.RulesFor(model)));
            Assert.Equal(Permissions.CustomersView, rule.Permission);

            var handlers = model.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => new[] { "OnGet", "OnPost", "OnPut", "OnDelete", "OnPatch" }.Any(verb => m.Name.StartsWith(verb, StringComparison.Ordinal)))
                .Select(m => m.Name);
            Assert.Equal(["OnGetAsync"], handlers);
        }
    }

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<string> PageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {url}: {(int)response.StatusCode} {response.Headers.Location}");
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>A link within the site with an email address in it (plain or escaped).</summary>
    [GeneratedRegex("href=\"/[^\"]*(@|%40)")]
    private static partial Regex LinkWithEmail();
}
