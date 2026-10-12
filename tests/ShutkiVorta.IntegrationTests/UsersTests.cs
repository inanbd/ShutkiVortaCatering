using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Razor.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Application.Features.TimeClock;
using ShutkiVorta.Domain.TimeClock;
using ShutkiVorta.Infrastructure.Email;
using ShutkiVorta.Infrastructure.Identity;
using ShutkiVorta.Infrastructure.Persistence;
using ShutkiVorta.IntegrationTests.Infrastructure;
using ShutkiVorta.Web.Infrastructure.Security;
using ShutkiVorta.Web.Pages.Admin.Users;
using static ShutkiVorta.IntegrationTests.Infrastructure.StaffTestHelper;

namespace ShutkiVorta.IntegrationTests;

/// <summary>
/// The Users pages and the Permissions page end to end: inviting people (and the link shown once), taking over unconfirmed
/// accounts, who may see and change what, and the guards. Tests in the shared app use their own people; tests that change a
/// role's permissions, Admins or the website address use apps of their own.
/// </summary>
[Collection("app")]
public sealed partial class UsersTests(TestServers servers)
{
    private const string Users = "/admin/users";
    private const string PermissionsPage = "/admin/permissions";
    private static readonly TimeSpan MailWait = TimeSpan.FromSeconds(15);

    // ---- Inviting someone new ----

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task AnInvitedEmployee_SetsTheirPassword_IsSignedIn_AndLandsOnTheTimeClock(string provider)
    {
        var app = servers.Get(provider);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var email = NewEmail("cook");

        var added = await AddUserAsync(admin, "Rahim Uddin", email, StaffRole.Employee, phone: "214-555-0101", jobTitle: "Cook");
        var userId = await UserIdAsync(app, email);
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        Assert.Equal($"{Users}/{userId}", added.Headers.Location!.OriginalString);

        // A new account: no password, email not confirmed, the staff role but no Customer role, and no staff access yet.
        var account = await AccountAsync(app, userId);
        Assert.Null(account.PasswordHash);
        Assert.False(account.EmailConfirmed);
        Assert.Equal(new[] { Roles.Employee }, await RoleNamesAsync(app, userId));
        Assert.False((await AccessAsync(app, userId)).IsStaff);
        await SetCustomPermissionsAsync(app, userId, []); // no dashboard: their landing page is the time clock

        // The link is shown once, with Copy and Share, and the email's delivery status; a reload no longer has it.
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}/{userId}"));
        Assert.Contains("Invitation link for Rahim", page);
        Assert.Contains("data-copy-link", page);
        Assert.Contains("href=\"sms:2145550101?&body=", page);
        Assert.Contains("Invitation email:", page);
        Assert.Contains("Settings → Email", page);
        var shown = InvitationLinkOnPage(page);
        Assert.StartsWith($"{BaseAddress}/account/reset-password?invite=1&email=", shown);
        var reloaded = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}/{userId}"));
        Assert.DoesNotContain(QueryValue(shown, "code"), reloaded);
        Assert.DoesNotContain("data-invite-link", reloaded);
        Assert.Contains("Rahim can't sign in yet", reloaded);

        // The same link is in the email (sensitive: it says it expires in 7 days), and nowhere in the audit log.
        var mail = await MailToAsync(app, email);
        Assert.Contains("Set your password", mail);
        Assert.Contains("expires in 7 days", mail);
        Assert.Equal(shown, InvitationLinkInEmail(mail));
        var audit = await AuditAsync(app, userId);
        Assert.Contains(audit, e => e.Action == StaffAuditActions.InvitationSent);
        Assert.DoesNotContain(audit, e => (e.Details ?? string.Empty).Contains(QueryValue(shown, "code"), StringComparison.Ordinal));

        // Following the link: "Set your password", then signed in and on the time clock with a welcome.
        var invitee = CreateClient(app);
        var form = WebUtility.HtmlDecode(await invitee.GetStringAsync(PathAndQuery(shown)));
        Assert.Contains("Set your password", form);
        var accepted = await AcceptInvitationAsync(invitee, shown, "Kitchen2026!");
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.Equal(StaffLanding.TimeClock, accepted.Headers.Location!.OriginalString);
        var clock = WebUtility.HtmlDecode(await invitee.GetStringAsync(StaffLanding.TimeClock));
        Assert.Contains("Welcome to the team!", clock);
        Assert.Contains("your phone will ask for your location", clock);
        Assert.True((await AccessAsync(app, userId)).IsStaff);
        Assert.True((await AccountAsync(app, userId)).EmailConfirmed);

        // The link works once.
        var again = WebUtility.HtmlDecode(await CreateClient(app).GetStringAsync(PathAndQuery(shown)));
        Assert.Contains(StaffInvitations.ExpiredOrUsed, again);
        Assert.DoesNotContain("Input.Password", again);

        // And they sign in like anyone else from now on.
        await SignInAsync(app, email, "Kitchen2026!");
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task AnInvitedManager_LandsOnTheDashboard(string provider)
    {
        var app = servers.Get(provider);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var email = NewEmail("manager");
        await AddUserAsync(admin, "Sumi Akter", email, StaffRole.Manager);

        var link = InvitationLinkInEmail(await MailToAsync(app, email));
        var accepted = await AcceptInvitationAsync(CreateClient(app), link, "Kitchen2026!");

        Assert.Equal(StaffLanding.Dashboard, accepted.Headers.Location!.OriginalString);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task AWeakPassword_OrAnExpiredLink_IsExplainedOnTheInvitationPage(string provider)
    {
        var app = servers.Get(provider);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var email = NewEmail("weak");
        await AddUserAsync(admin, "Weak Password", email, StaffRole.Employee);
        var link = InvitationLinkInEmail(await MailToAsync(app, email));

        // Identity's password rules, on the page; the link still works afterwards.
        var weak = await AcceptInvitationAsync(CreateClient(app), link, "aaaaaaaa");
        Assert.Equal(HttpStatusCode.OK, weak.StatusCode);
        Assert.Contains("digit", WebUtility.HtmlDecode(await weak.Content.ReadAsStringAsync()));

        // A made-up or damaged code: the friendly message, before anyone types a password (and if they post anyway).
        var damaged = link.Replace("code=", "code=x", StringComparison.Ordinal);
        Assert.Contains(StaffInvitations.ExpiredOrUsed, WebUtility.HtmlDecode(await CreateClient(app).GetStringAsync(PathAndQuery(damaged))));
        var client = CreateClient(app);
        var refused = await AcceptInvitationAsync(client, damaged, "Kitchen2026!", formHtml: await client.GetStringAsync(PathAndQuery(link)));
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains(StaffInvitations.ExpiredOrUsed, WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));

        Assert.Equal(HttpStatusCode.Redirect, (await AcceptInvitationAsync(CreateClient(app), link, "Kitchen2026!")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task ResendingAnInvitation_KillsTheEarlierLink(string provider)
    {
        var app = servers.Get(provider);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var email = NewEmail("resend");
        await AddUserAsync(admin, "Resend Me", email, StaffRole.Employee);
        var userId = await UserIdAsync(app, email);
        var first = InvitationLinkInEmail(await MailToAsync(app, email));
        var firstClient = CreateClient(app);
        var firstForm = await firstClient.GetStringAsync(PathAndQuery(first)); // opened before the resend

        var since = DateTime.UtcNow;
        var resent = await PostUserAsync(admin, userId, "Resend", []);
        Assert.Equal(HttpStatusCode.Redirect, resent.StatusCode);
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}/{userId}"));
        Assert.Contains("A new invitation is ready", page);
        var shown = InvitationLinkOnPage(page);

        var second = InvitationLinkInEmail(await MailToAsync(app, email, since));
        Assert.NotEqual(first, second);
        Assert.Equal(shown, second);

        Assert.Contains(StaffInvitations.ExpiredOrUsed, WebUtility.HtmlDecode(await CreateClient(app).GetStringAsync(PathAndQuery(first))));
        var refused = await AcceptInvitationAsync(firstClient, first, "Kitchen2026!", firstForm);
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains(StaffInvitations.ExpiredOrUsed, WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Redirect, (await AcceptInvitationAsync(CreateClient(app), second, "Kitchen2026!")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task AMistypedEmail_IsFixedBeforeThePasswordIsSet_AndTheOldLinkStopsWorking(string provider)
    {
        var app = servers.Get(provider);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var typo = NewEmail("typo");
        await AddUserAsync(admin, "Typo Person", typo, StaffRole.Employee);
        var userId = await UserIdAsync(app, typo);
        var oldLink = InvitationLinkInEmail(await MailToAsync(app, typo));

        var fixedEmail = NewEmail("fixed");
        Assert.Equal(HttpStatusCode.Redirect, (await PostUserAsync(admin, userId, "Email", new() { ["email"] = fixedEmail })).StatusCode);
        var newLink = InvitationLinkInEmail(await MailToAsync(app, fixedEmail));

        var account = await AccountAsync(app, userId);
        Assert.Equal(fixedEmail, account.Email);
        Assert.Equal(fixedEmail, account.UserName);
        Assert.Equal(fixedEmail.ToUpperInvariant(), account.NormalizedEmail);
        Assert.Contains(await AuditAsync(app, userId), e => e.Action == StaffAuditActions.EmailChanged && e.Details!.Contains(typo));
        Assert.Contains(StaffInvitations.ExpiredOrUsed, WebUtility.HtmlDecode(await CreateClient(app).GetStringAsync(PathAndQuery(oldLink))));
        Assert.Equal(HttpStatusCode.Redirect, (await AcceptInvitationAsync(CreateClient(app), newLink, "Kitchen2026!")).StatusCode);

        // With a password set, the email can no longer be changed here.
        await PostUserAsync(admin, userId, "Email", new() { ["email"] = NewEmail("later") });
        Assert.Contains("already set a password", WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}/{userId}")));
        Assert.Equal(fixedEmail, (await AccountAsync(app, userId)).Email);
    }

    // ---- Existing accounts ----

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task AnUnconfirmedSelfRegisteredAccount_GetsNoAccess_UntilTheInvitationIsUsed(string provider)
    {
        var app = servers.Get(provider);
        var email = NewEmail("squatter");
        var userId = await CreateAccountAsync(app, email, "Squatter Name", "Mine12345!", emailConfirmed: false);
        var squatter = await SignInAsync(app, email, "Mine12345!"); // the site does not require confirmed emails by default

        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var added = await AddUserAsync(admin, "Real Manager", email, StaffRole.Manager);
        Assert.Equal($"{Users}/{userId}", added.Headers.Location!.OriginalString); // no confirmation step: nobody proved they own it

        // Their password is gone, their session is over, and their name is the one the admin entered.
        var account = await AccountAsync(app, userId);
        Assert.Null(account.PasswordHash);
        Assert.Equal("Real Manager", account.FullName);
        AssertSignInRequired(await squatter.GetAsync("/admin"));
        Assert.NotEqual(HttpStatusCode.Redirect, (await LoginAsync(CreateClient(app), email, "Mine12345!")).StatusCode);
        Assert.Contains(await AuditAsync(app, userId), e => e.Action == StaffAuditActions.InvitationSent && e.Details!.Contains("never confirmed"));

        // Only the mailbox's owner gets in, through the link.
        var link = InvitationLinkInEmail(await MailToAsync(app, email));
        var owner = CreateClient(app);
        var accepted = await AcceptInvitationAsync(owner, link, "Owner12345!");
        Assert.Equal(StaffLanding.Dashboard, accepted.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/admin")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task TheSquattersOldSession_EndsAtTheTakeover_AndAProfileSaveCannotRevive_ItAsTheNewAdmin(string provider)
    {
        var app = servers.Get(provider);
        var email = NewEmail("window");
        var userId = await CreateAccountAsync(app, email, "Squatter Name", "Mine12345!", emailConfirmed: false);
        var squatter = await SignInAsync(app, email, "Mine12345!");
        var profileForm = await squatter.GetStringAsync("/account/manage"); // opened before the takeover

        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        await AddUserAsync(admin, "Real Admin", email, StaffRole.Admin, phone: "214-555-0999");
        var owner = CreateClient(app);
        var accepted = await AcceptInvitationAsync(owner, InvitationLinkInEmail(await MailToAsync(app, email)), "Owner12345!");
        Assert.Equal(StaffLanding.Dashboard, accepted.Headers.Location!.OriginalString);

        // Well within Identity's 5-minute check, the old cookie is signed out on its next request: no profile (with the phone
        // the admin entered), no profile save (which used to re-issue the cookie with the new stamp), and no admin pages.
        AssertSignInRequired(await squatter.GetAsync("/account/manage"));
        var saved = await PostFormAsync(squatter, "/account/manage", profileForm, new Dictionary<string, string>
        {
            ["Input.FullName"] = "Squatter Renamed This Admin",
            ["Input.PhoneNumber"] = "2145550000",
        });
        Assert.NotEqual(HttpStatusCode.OK, saved.StatusCode);
        Assert.DoesNotContain("/account/manage", saved.Headers.Location?.OriginalString ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        AssertSignInRequired(await squatter.GetAsync("/admin"));
        AssertSignInRequired(await squatter.GetAsync("/admin/settings"));
        var account = await AccountAsync(app, userId);
        Assert.Equal("Real Admin", account.FullName);
        Assert.Equal("214-555-0999", account.PhoneNumber);

        // The owner's own session is untouched, and saving their profile keeps them signed in.
        var ownForm = await owner.GetStringAsync("/account/manage");
        Assert.Contains("214-555-0999", ownForm);
        var ownSave = await PostFormAsync(owner, "/account/manage", ownForm, new Dictionary<string, string>
        {
            ["Input.FullName"] = "Real Admin",
            ["Input.PhoneNumber"] = "214-555-0999",
        });
        Assert.Equal(HttpStatusCode.Redirect, ownSave.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/admin/settings")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task RefreshingASignIn_WhoseStampChangedMeanwhile_SignsItOut_InsteadOfIssuingTheNewStamp(string provider)
    {
        var app = servers.Get(provider);
        var email = NewEmail("race");
        var userId = await CreateAccountAsync(app, email, "Race Squatter", "Mine12345!", emailConfirmed: false);

        using var scope = app.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        accessor.HttpContext = context;
        try
        {
            var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            context.User = await signIn.CreateUserPrincipalAsync((await users.FindByIdAsync(userId))!);

            // The request passed the stamp check, then the account was taken over as an invitation before the profile save
            // refreshed the sign-in.
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            Assert.True((await identity.ResetToInvitationAsync(userId)).Succeeded);
            await identity.RefreshSignInAsync(userId);

            var cookie = context.Response.Headers.SetCookie.ToString();
            Assert.Contains("sv_auth=;", cookie); // deleted, not re-issued
            Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task AConfirmedCustomer_IsAddedAfterTheAdminConfirmsWhoItIs_AndKeepsTheirPassword(string provider)
    {
        var app = servers.Get(provider);
        var email = NewEmail("customer");
        var userId = await CreateAccountAsync(app, email, "Nasrin Customer", "Mine12345!", emailConfirmed: true);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);

        // First the confirmation step: who it is, and nothing changed yet.
        var asked = await AddUserAsync(admin, "Someone Else", email, StaffRole.Employee);
        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        var step = WebUtility.HtmlDecode(await asked.Content.ReadAsStringAsync());
        Assert.Contains("This email already has an account", step);
        Assert.Contains("Nasrin Customer", step);
        Assert.Contains("Yes, add Nasrin Customer as Employee", step);
        Assert.Empty(await RoleNamesAsync(app, userId, staffOnly: true));

        // Confirming a different account id (e.g. a stale form) asks again.
        Assert.Equal(HttpStatusCode.OK, (await AddUserAsync(admin, "Someone Else", email, StaffRole.Employee, confirmedUserId: Guid.NewGuid().ToString())).StatusCode);

        var confirmed = await AddUserAsync(admin, "Someone Else", email, StaffRole.Employee, confirmedUserId: userId);
        Assert.Equal($"{Users}/{userId}", confirmed.Headers.Location!.OriginalString);
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}/{userId}"));
        Assert.Contains("Nasrin is on the staff", page);
        Assert.DoesNotContain("data-invite-link", page);

        var mail = await MailToAsync(app, email);
        Assert.Contains("You've been added to the staff", mail);
        Assert.Equal("Nasrin Customer", (await AccountAsync(app, userId)).FullName);
        var customer = await SignInAsync(app, email, "Mine12345!");
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync(StaffLanding.TimeClock)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task AddingSomeoneAlreadyOnTheStaff_IsRefused_WithALinkToTheirPage(string provider)
    {
        var app = servers.Get(provider);
        var manager = await CreateStaffAsync(app, StaffRole.Manager);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);

        var response = await AddUserAsync(admin, "Demote Me", manager.Email.ToUpperInvariant(), StaffRole.Employee);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("is already on the staff", html);
        Assert.Contains($"href=\"{Users}/{manager.UserId}\"", html);
        Assert.Equal(StaffRole.Manager, (await AccessAsync(app, manager.UserId)).Role);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task AddingTheSamePersonFromSeveralTabsAtOnce_CreatesOneAccount_AndNeverFails(string provider)
    {
        var app = servers.Get(provider);
        var email = NewEmail("race");

        // The store reports a second insert of the same email as a duplicate (what Identity's own check says), not a crash.
        using (var scope = app.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IUserStore<ApplicationUser>>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser NewUser() => new()
            {
                UserName = email, NormalizedUserName = users.NormalizeName(email), Email = email, NormalizedEmail = users.NormalizeEmail(email),
                FullName = "Race", SecurityStamp = Guid.NewGuid().ToString(), CreatedAtUtc = DateTime.UtcNow,
            };
            Assert.True((await store.CreateAsync(NewUser(), CancellationToken.None)).Succeeded);
            var duplicate = await store.CreateAsync(NewUser(), CancellationToken.None);
            Assert.False(duplicate.Succeeded);
            Assert.Equal("DuplicateEmail", Assert.Single(duplicate.Errors).Code);
            await users.DeleteAsync((await users.FindByEmailAsync(email))!);
        }

        // Six posts of the same new person at once: one account, and every answer a page, never an error.
        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var form = await admin.GetStringAsync("/admin/users/add");
        var fields = new Dictionary<string, string>
        {
            ["Input.FullName"] = "Race Person",
            ["Input.Email"] = email,
            ["Input.PhoneNumber"] = string.Empty,
            ["Input.JobTitle"] = string.Empty,
            ["Input.Role"] = nameof(StaffRole.Employee),
        };
        var answers = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostFormAsync(admin, "/admin/users/add", form, fields)));
        Assert.All(answers, a => Assert.True(a.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK, $"{(int)a.StatusCode}"));
        Assert.Contains(answers, a => a.StatusCode == HttpStatusCode.Redirect);
        var userId = await UserIdAsync(app, email);
        Assert.Equal(1, (await QueryAsync<int>(app, "SELECT COUNT(*) FROM Users WHERE NormalizedEmail = @Email", new { Email = email.ToUpperInvariant() })).Single());
        Assert.Equal(StaffRole.Employee, (await MemberAsync(app, userId)).Role);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task AddingWithBadInput_ShowsTheProblems_AndCreatesNothing(string provider)
    {
        var app = servers.Get(provider);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var email = NewEmail("bad");

        var response = await AddUserAsync(admin, "", email, StaffRole.Employee, phone: "12");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Please enter their name.", html);
        Assert.Null(await FindUserIdAsync(app, email));
    }

    // ---- Who may see and change what ----

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task StaffView_SeesTheListReadOnly_AndGets403OnEveryAdminAction(string provider)
    {
        var app = servers.Get(provider);
        var viewer = await CreateStaffAsync(app, StaffRole.Manager, [Permissions.StaffView]);
        var colleague = await CreateStaffAsync(app, StaffRole.Employee);
        var client = await SignInAsync(app, viewer.Email);

        var list = WebUtility.HtmlDecode(await client.GetStringAsync($"{Users}?q={colleague.Email}"));
        Assert.Contains(colleague.FullName, list);
        Assert.Contains("Only Admins can add people", list);
        Assert.DoesNotContain("href=\"/admin/users/add\"", list);
        Assert.DoesNotContain("href=\"/admin/permissions\"", list);

        // Their page shows what they can do, and no form to change anything.
        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"{Users}/{colleague.UserId}"));
        Assert.Contains("can do", page);
        Assert.Contains("Recent changes", page);
        Assert.All(UserPageActions, handler => Assert.DoesNotContain($"?handler={handler}\"", page));
        Assert.DoesNotContain("Remove from staff", page);

        AssertDenied(await client.GetAsync("/admin/users/add"));
        AssertDenied(await client.GetAsync(PermissionsPage));
        AssertDenied(await client.GetAsync($"{Users}/{colleague.UserId}?handler=EmailStatus"));
        foreach (var handler in UserPageActions)
        {
            AssertDenied(await PostFormAsync(client, $"{Users}/{colleague.UserId}?handler={handler}", page, new Dictionary<string, string>
            {
                ["version"] = "0", ["role"] = "Admin", ["email"] = NewEmail("x"), ["mode"] = "custom", ["levels[staff]"] = "view",
                ["details.FullName"] = "Changed",
            }));
        }

        AssertDenied(await PostFormAsync(client, "/admin/users/add", page, new Dictionary<string, string>
        {
            ["Input.FullName"] = "Sneaky", ["Input.Email"] = NewEmail("sneaky"), ["Input.Role"] = "Admin",
        }));
        AssertDenied(await PostFormAsync(client, $"{PermissionsPage}?handler=Save", page, new Dictionary<string, string> { ["role"] = "Manager", ["revision"] = "0" }));
        AssertDenied(await PostFormAsync(client, $"{PermissionsPage}?handler=Restore", page, new Dictionary<string, string> { ["role"] = "Manager", ["revision"] = "0" }));
        Assert.Equal(StaffRole.Employee, (await AccessAsync(app, colleague.UserId)).Role);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task WithoutStaffView_TheUsersPagesAreRefused(string provider)
    {
        var app = servers.Get(provider);
        var employee = await CreateStaffAsync(app, StaffRole.Employee, [Permissions.OrdersView]);
        var client = await SignInAsync(app, employee.Email);

        AssertDenied(await client.GetAsync(Users));
        AssertDenied(await client.GetAsync($"{Users}/{employee.UserId}"));
        AssertSignInRequired(await CreateClient(app).GetAsync(Users));
        var customerEmail = NewEmail("customer");
        await CreateAccountAsync(app, customerEmail, "Just A Customer", "Mine12345!", emailConfirmed: true);
        AssertNotStaff(await (await SignInAsync(app, customerEmail, "Mine12345!")).GetAsync(Users));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task AnAccountThatWasNeverStaff_IsNotFound(string provider)
    {
        var app = servers.Get(provider);
        var customerEmail = NewEmail("customer");
        var customerId = await CreateAccountAsync(app, customerEmail, "Never Staff", "Mine12345!", emailConfirmed: true);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Users}/{customerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Users}/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Users}/not-a-guid")).StatusCode);
        var page = await admin.GetStringAsync(Users);
        Assert.Equal(HttpStatusCode.NotFound, (await PostFormAsync(admin, $"{Users}/{customerId}?handler=Role", page,
            new Dictionary<string, string> { ["role"] = "Admin", ["version"] = "0" })).StatusCode);
        Assert.Empty(await RoleNamesAsync(app, customerId, staffOnly: true));
    }

    // ---- Roles and permissions on a person's page ----

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task ChangingTheRole_OnThePage_ResetsCustomPermissions_AndAStaleFormIsRefused(string provider)
    {
        var app = servers.Get(provider);
        var person = await CreateStaffAsync(app, StaffRole.Employee, [Permissions.OrdersView, Permissions.CustomersView]);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}/{person.UserId}"));
        Assert.Contains("Custom permissions", page);
        Assert.Contains("custom permissions to the new role's", page);
        var version = await VersionAsync(app, person.UserId);

        var changed = await PostFormAsync(admin, $"{Users}/{person.UserId}?handler=Role", page,
            new Dictionary<string, string> { ["role"] = "Manager", ["version"] = version.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        Assert.Contains("Role changed to Manager.", WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}/{person.UserId}")));

        var access = await AccessAsync(app, person.UserId);
        Assert.Equal(StaffRole.Manager, access.Role);
        Assert.False(access.UsesCustomPermissions);
        Assert.True(access.Permissions.SetEquals(PermissionCatalog.Defaults(StaffRole.Manager)));
        Assert.Contains(await AuditAsync(app, person.UserId), e => e.Action == StaffAuditActions.RoleChanged && e.Details!.Contains("custom permissions were removed"));

        // The same form again (its version is now old): nothing changes.
        await PostFormAsync(admin, $"{Users}/{person.UserId}?handler=Role", page,
            new Dictionary<string, string> { ["role"] = "Employee", ["version"] = version.ToString() });
        Assert.Contains("Someone changed this person", WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}/{person.UserId}")));
        Assert.Equal(StaffRole.Manager, (await AccessAsync(app, person.UserId)).Role);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task TwoAdminsChangingTheSamePerson_AtTheSameMoment_OnlyTheFirstGetsThrough(string provider)
    {
        var app = servers.Get(provider);
        var first = await SignInAsync(app, AppFactory.AdminEmail);
        var second = await SignInAsync(app, (await CreateStaffAsync(app, StaffRole.Admin)).Email);

        for (var round = 0; round < 3; round++)
        {
            var person = await CreateStaffAsync(app, StaffRole.Employee);
            var url = $"{Users}/{person.UserId}";
            var version = (await VersionAsync(app, person.UserId)).ToString();
            var (firstPage, secondPage) = (await first.GetStringAsync(url), await second.GetStringAsync(url));

            // Both pages showed the same version: one change gets through, the other is told to reload.
            await Task.WhenAll(
                PostFormAsync(first, $"{url}?handler=Role", firstPage, new Dictionary<string, string> { ["role"] = "Manager", ["version"] = version }),
                PostFormAsync(second, $"{url}?handler=Role", secondPage, new Dictionary<string, string> { ["role"] = "Admin", ["version"] = version }));

            Assert.Single(await AuditAsync(app, person.UserId), e => e.Action == StaffAuditActions.RoleChanged);
            var told = new[] { await first.GetStringAsync(url), await second.GetStringAsync(url) };
            Assert.Single(told, html => WebUtility.HtmlDecode(html).Contains("Someone changed this person"));
        }
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task CustomPermissions_SetOnThePage_ApplyOnTheNextRequest(string provider)
    {
        var app = servers.Get(provider);
        var employee = await CreateStaffAsync(app, StaffRole.Employee);
        var client = await SignInAsync(app, employee.Email);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/inventory")).StatusCode);
        AssertDenied(await client.GetAsync("/admin/inquiries"));

        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}/{employee.UserId}"));
        Assert.Contains("Follow the Employee permissions", page);
        // The selects start from what the person can do now (the Employee defaults).
        Assert.Matches("<option value=\"view\" selected=\"selected\">View \\(default\\)</option>", page);

        var levels = PermissionLevels.Of(PermissionCatalog.Defaults(StaffRole.Employee)).ToDictionary();

        // "Follow the role" saved again changes nothing: no new version, no audit entry.
        var version = await VersionAsync(app, employee.UserId);
        await PostUserAsync(admin, employee.UserId, "Permissions", Levels(levels, "role", version));
        Assert.Contains(StaffUi.NothingChanged, WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}/{employee.UserId}")));
        Assert.Equal(version, await VersionAsync(app, employee.UserId));
        Assert.DoesNotContain(await AuditAsync(app, employee.UserId), e => e.Action == StaffAuditActions.PermissionsChanged);

        levels["inventory"] = PermissionCatalog.NoAccess;
        levels["inquiries"] = "view";
        var saved = await PostUserAsync(admin, employee.UserId, "Permissions", Levels(levels, "custom", await VersionAsync(app, employee.UserId)));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        AssertDenied(await client.GetAsync("/admin/inventory"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/inquiries")).StatusCode);
        page = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}/{employee.UserId}"));
        Assert.Contains("Custom permissions saved.", page);
        Assert.Contains("Their own permissions", page);

        // Back to the role's permissions.
        await PostUserAsync(admin, employee.UserId, "Permissions", Levels(levels, "role", await VersionAsync(app, employee.UserId)));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/inventory")).StatusCode);
        AssertDenied(await client.GetAsync("/admin/inquiries"));

        // An Admin's page has no permission editor: Admins have everything.
        Assert.DoesNotContain("handler=Permissions", await admin.GetStringAsync($"{Users}/{(await CreateStaffAsync(app, StaffRole.Admin)).UserId}"));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task UnlockingSignIn_LetsALockedOutPersonInAgain(string provider)
    {
        var app = servers.Get(provider);
        var employee = await CreateStaffAsync(app, StaffRole.Employee);
        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(CreateClient(app), employee.Email, "Wrong-password-1");
        }

        Assert.NotEqual(HttpStatusCode.Redirect, (await LoginAsync(CreateClient(app), employee.Email)).StatusCode);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}/{employee.UserId}"));
        Assert.Contains("Locked out", page);

        Assert.Equal(HttpStatusCode.Redirect, (await PostUserAsync(admin, employee.UserId, "Unlock", [])).StatusCode);
        await SignInAsync(app, employee.Email);
        Assert.Contains(await AuditAsync(app, employee.UserId), e => e.Action == StaffAuditActions.AccountUnlocked);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task EditingDetails_SavesNamePhoneAndJobTitle(string provider)
    {
        var app = servers.Get(provider);
        var employee = await CreateStaffAsync(app, StaffRole.Employee);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var version = await VersionAsync(app, employee.UserId);

        var saved = await PostUserAsync(admin, employee.UserId, "Details", new()
        {
            ["details.FullName"] = "Karim Hossain", ["details.PhoneNumber"] = "(214) 555-0199", ["details.JobTitle"] = "Delivery", ["version"] = version.ToString(),
        });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var member = await MemberAsync(app, employee.UserId);
        Assert.Equal("Karim Hossain", member.FullName);
        Assert.Equal("(214) 555-0199", member.PhoneNumber);
        Assert.Equal("Delivery", member.JobTitle);

        // A bad phone number: the form comes back with the problem and what was typed.
        var refused = await PostUserAsync(admin, employee.UserId, "Details", new()
        {
            ["details.FullName"] = "Karim Typo", ["details.PhoneNumber"] = "12", ["version"] = (await VersionAsync(app, employee.UserId)).ToString(),
        });
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        var html = WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync());
        Assert.Contains("Please enter a valid phone number.", html);
        Assert.Contains("value=\"Karim Typo\"", html);
        Assert.Equal("Karim Hossain", (await MemberAsync(app, employee.UserId)).FullName);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task DetailsSavedFromAStalePage_DoNotOverwriteAnotherAdminsChange(string provider)
    {
        var app = servers.Get(provider);
        var employee = await CreateStaffAsync(app, StaffRole.Employee);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var version = (await VersionAsync(app, employee.UserId)).ToString();

        // Only the name and phone change (they live on the account, not the staff profile): the version still moves on.
        Assert.Equal(HttpStatusCode.Redirect, (await PostUserAsync(admin, employee.UserId, "Details", new()
        {
            ["details.FullName"] = "First Change", ["details.PhoneNumber"] = "(214) 555-0123", ["version"] = version,
        })).StatusCode);
        Assert.NotEqual(version, (await VersionAsync(app, employee.UserId)).ToString());

        // A second form opened before that change: refused, and the first change stays.
        var stale = await PostUserAsync(admin, employee.UserId, "Details", new()
        {
            ["details.FullName"] = "Stale Change", ["details.PhoneNumber"] = "", ["version"] = version,
        });
        Assert.Contains("Someone changed this person", WebUtility.HtmlDecode(await stale.Content.ReadAsStringAsync()));
        var member = await MemberAsync(app, employee.UserId);
        Assert.Equal("First Change", member.FullName);
        Assert.Equal("(214) 555-0123", member.PhoneNumber);
    }

    // ---- Removing and re-adding ----

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task RemovingSomeone_ClosesTheirOpenShift_AndReAddingThemKeepsTheirPassword(string provider)
    {
        var app = servers.Get(provider);
        var employee = await CreateStaffAsync(app, StaffRole.Employee);
        var employeeClient = await SignInAsync(app, employee.Email);
        await PostFormAsync(employeeClient, "/admin/time-clock?handler=ClockIn", await employeeClient.GetStringAsync("/admin/time-clock"),
            new Dictionary<string, string> { ["Punch.Status"] = "NotProvided" });
        Assert.NotNull(await OpenShiftStartAsync(app, employee.UserId));

        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}/{employee.UserId}"));
        Assert.Contains("On the clock", page);
        Assert.Contains($"href=\"/admin/time-clock/team?user={employee.UserId}\"", page);
        Assert.Contains("Their open shift is closed now and flagged for review.", page);

        Assert.Equal(HttpStatusCode.Redirect, (await PostUserAsync(admin, employee.UserId, "Remove", new() { ["version"] = (await VersionAsync(app, employee.UserId)).ToString() })).StatusCode);
        Assert.Null(await OpenShiftStartAsync(app, employee.UserId));
        AssertNotStaff(await employeeClient.GetAsync("/admin"));
        var former = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}?show=former&q={employee.Email}"));
        Assert.Contains(employee.FullName, former);
        Assert.Contains("Re-add", former);
        page = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}/{employee.UserId}"));
        Assert.Contains("Former staff", page);
        Assert.Contains("Re-add to the staff", page);

        // They still have their password and a confirmed email: back in without an invitation.
        Assert.Equal(HttpStatusCode.Redirect, (await PostUserAsync(admin, employee.UserId, "ReAdd", new() { ["role"] = "Employee" })).StatusCode);
        Assert.Contains("is on the staff again", WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}/{employee.UserId}")));
        Assert.Equal(HttpStatusCode.OK, (await employeeClient.GetAsync(StaffLanding.TimeClock)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task RemovingSomeoneWhoNeverSetAPassword_EndsTheirInvitation(string provider)
    {
        var app = servers.Get(provider);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var email = NewEmail("leaver");
        await AddUserAsync(admin, "Never Started", email, StaffRole.Employee);
        var userId = await UserIdAsync(app, email);
        var link = InvitationLinkInEmail(await MailToAsync(app, email));

        await PostUserAsync(admin, userId, "Remove", new() { ["version"] = (await VersionAsync(app, userId)).ToString() });

        Assert.Null((await AccessAsync(app, userId)).Role);
        Assert.Contains(StaffInvitations.ExpiredOrUsed, WebUtility.HtmlDecode(await CreateClient(app).GetStringAsync(PathAndQuery(link))));

        // Former staff have no invitation waiting: neither the list nor their page says there is one.
        var former = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}?show=former&q={email}"));
        Assert.Contains("Never Started", former);
        Assert.DoesNotContain("Invitation pending", former);
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}/{userId}"));
        Assert.Contains("Former staff", page);
        Assert.DoesNotContain("Invitation pending", page);
        Assert.DoesNotContain("invitation pending", page);

        // Re-adding them sends a new invitation.
        var since = DateTime.UtcNow;
        await PostUserAsync(admin, userId, "ReAdd", new() { ["role"] = "Employee" });
        Assert.Contains("data-invite-link", await admin.GetStringAsync($"{Users}/{userId}"));
        Assert.NotEqual(link, InvitationLinkInEmail(await MailToAsync(app, email, since)));
    }

    // ---- Guards, site address and the Permissions page (apps of their own) ----

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task TheOwnRoleAndLastAdminRules_ShowAsFriendlyErrors(string provider)
    {
        await using var app = new AppFactory(provider);
        var seedAdmin = await UserIdAsync(app, AppFactory.AdminEmail);
        var actor = await CreateStaffAsync(app, StaffRole.Admin);
        var client = await SignInAsync(app, actor.Email);

        // Your own page has no role or remove forms, and posting them anyway is refused with a message.
        var own = WebUtility.HtmlDecode(await client.GetStringAsync($"{Users}/{actor.UserId}"));
        Assert.Contains("You can't change your own role", own);
        Assert.DoesNotContain("handler=Role", own);
        Assert.DoesNotContain("handler=Remove", own);
        await PostFormAsync(client, $"{Users}/{actor.UserId}?handler=Role", own,
            new Dictionary<string, string> { ["role"] = "Employee", ["version"] = (await VersionAsync(app, actor.UserId)).ToString() });
        Assert.Contains("You can't change your own role or remove yourself", WebUtility.HtmlDecode(await client.GetStringAsync($"{Users}/{actor.UserId}")));
        await PostFormAsync(client, $"{Users}/{actor.UserId}?handler=Remove", own,
            new Dictionary<string, string> { ["version"] = (await VersionAsync(app, actor.UserId)).ToString() });
        Assert.Equal(StaffRole.Admin, (await AccessAsync(app, actor.UserId)).Role);

        // While the acting admin is locked out of signing in (their session still works), the seeded admin is the only one
        // who can sign in: demoting them is refused, with the reason on the page.
        await ExecuteAsync(app, "UPDATE Users SET LockoutEndUtc = @Until WHERE Id = @Id", new { Until = DateTime.UtcNow.AddHours(1), Id = actor.UserId });
        var page = await client.GetStringAsync($"{Users}/{seedAdmin}");
        await PostFormAsync(client, $"{Users}/{seedAdmin}?handler=Role", page,
            new Dictionary<string, string> { ["role"] = "Manager", ["version"] = (await VersionAsync(app, seedAdmin)).ToString() });
        Assert.Contains("at least one Admin who can sign in", WebUtility.HtmlDecode(await client.GetStringAsync($"{Users}/{seedAdmin}")));
        Assert.Equal(StaffRole.Admin, (await AccessAsync(app, seedAdmin)).Role);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task WithoutAWebsiteAddress_NoStaffEmailIsSent_AndTheLinkUsesTheAdminsOwnAddress(string provider)
    {
        await using var app = new AppFactory(provider, new Dictionary<string, string?> { ["Site:BaseUrl"] = "" });
        var admin = app.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://kitchen-laptop.test") });
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(admin, AppFactory.AdminEmail)).StatusCode);
        var email = NewEmail("nobase");

        await AddUserAsync(admin, "No Base", email, StaffRole.Employee);
        var userId = await UserIdAsync(app, email);
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}/{userId}"));

        Assert.Contains("Not emailed: set the Website address in", page);
        Assert.Contains("to email invitations.", page);
        Assert.StartsWith("https://kitchen-laptop.test/account/reset-password?invite=1", InvitationLinkOnPage(page));
        Assert.Contains(await AuditAsync(app, userId), e => e.Action == StaffAuditActions.InvitationSent && e.Details!.Contains("Not emailed"));
        await using var scope = app.Services.CreateAsyncScope();
        var emails = scope.ServiceProvider.GetRequiredService<IStaffEmailLog>();
        Assert.Null(await emails.GetLatestAsync(email, [StaffInvitations.InvitationSubject, StaffInvitations.AddedSubject]));

        // Someone who keeps their password isn't emailed either: its sign-in link would also use the request's address.
        var customerEmail = NewEmail("nobase-customer");
        var customerId = await CreateAccountAsync(app, customerEmail, "Kept Password", "Mine12345!", emailConfirmed: true);
        Assert.Equal(HttpStatusCode.Redirect, (await AddUserAsync(admin, "Kept Password", customerEmail, StaffRole.Employee, confirmedUserId: customerId)).StatusCode);
        var added = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}/{customerId}"));
        Assert.Contains("Kept is on the staff", added);
        Assert.Contains("Not emailed: set the Website address in", added);
        Assert.Contains("tell them yourself", added);
        Assert.DoesNotContain("We emailed them", added);
        Assert.Equal(StaffRole.Employee, (await AccessAsync(app, customerId)).Role);
        Assert.Null(await emails.GetLatestAsync(customerEmail, [StaffInvitations.InvitationSubject, StaffInvitations.AddedSubject]));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task ThePermissionsPage_SavesPerRole_RefusesAStaleSave_AndRestoresTheDefaults(string provider)
    {
        await using var app = new AppFactory(provider);
        var employee = await CreateStaffAsync(app, StaffRole.Employee);
        var custom = await CreateStaffAsync(app, StaffRole.Employee, [Permissions.OrdersView]);
        var employeeClient = await SignInAsync(app, employee.Email);
        AssertDenied(await employeeClient.GetAsync("/admin/inquiries"));

        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync(PermissionsPage));
        Assert.Contains("Everything", page);
        Assert.Contains("Admins only", page);
        Assert.Contains("Clock in and out", page);
        Assert.Contains("Not affected", page);
        Assert.Contains($"href=\"{Users}/{custom.UserId}\"", page);
        Assert.Contains("form=\"save-Employee\"", page);

        var levels = PermissionLevels.Of(PermissionCatalog.Defaults(StaffRole.Employee)).ToDictionary();

        // Saving without changing anything writes nothing: the revision stays (the save below at revision 0 still works) and
        // the recent changes stay empty.
        var unchanged = Levels(levels, mode: null, version: null);
        unchanged["role"] = "Employee";
        unchanged["revision"] = "0";
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(admin, $"{PermissionsPage}?handler=Save", page, unchanged)).StatusCode);
        var afterUnchanged = WebUtility.HtmlDecode(await admin.GetStringAsync(PermissionsPage));
        Assert.Contains(StaffUi.NothingChanged, afterUnchanged);
        Assert.DoesNotContain("Employee permissions: No changes", afterUnchanged);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IStaffAuditLog>();
            Assert.Empty(await audit.ListByActionsAsync([StaffAuditActions.RoleDefaultsChanged], 10));
        }

        levels["inquiries"] = "view";
        var fields = Levels(levels, mode: null, version: null);
        fields["role"] = "Employee";
        fields["revision"] = "0";

        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(admin, $"{PermissionsPage}?handler=Save", page, fields)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await employeeClient.GetAsync("/admin/inquiries")).StatusCode); // the next request
        Assert.False((await AccessAsync(app, custom.UserId)).Can(Permissions.InquiriesView));          // custom: not affected
        page = WebUtility.HtmlDecode(await admin.GetStringAsync(PermissionsPage));
        Assert.Contains("Employee permissions saved.", page);
        Assert.Contains("Inquiries: No access → View", page);

        // The same form again (revision 0 is stale now) is refused, and nothing changes.
        levels["inquiries"] = "manage";
        var stale = Levels(levels, mode: null, version: null);
        stale["role"] = "Employee";
        stale["revision"] = "0";
        await PostFormAsync(admin, $"{PermissionsPage}?handler=Save", page, stale);
        Assert.Contains("Someone changed these permissions", WebUtility.HtmlDecode(await admin.GetStringAsync(PermissionsPage)));
        Assert.False((await AccessAsync(app, employee.UserId)).Can(Permissions.InquiriesManage));

        // Restore defaults (at the current revision).
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(admin, $"{PermissionsPage}?handler=Restore", page,
            new Dictionary<string, string> { ["role"] = "Employee", ["revision"] = "1" })).StatusCode);
        AssertDenied(await employeeClient.GetAsync("/admin/inquiries"));
        page = WebUtility.HtmlDecode(await admin.GetStringAsync(PermissionsPage));
        Assert.Contains("back to the defaults", page);
        Assert.Contains("restored to the defaults", page);

        // Admin can't be configured.
        await PostFormAsync(admin, $"{PermissionsPage}?handler=Save", page, new Dictionary<string, string> { ["role"] = "Admin", ["revision"] = "0" });
        Assert.Contains("Admins always have every permission", WebUtility.HtmlDecode(await admin.GetStringAsync(PermissionsPage)));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task TheStaffList_FiltersByRoleAndSearch_AndShowsWhatStandsOut(string provider)
    {
        await using var app = new AppFactory(provider);
        var manager = await CreateStaffAsync(app, StaffRole.Manager);
        var employee = await CreateStaffAsync(app, StaffRole.Employee, [Permissions.OrdersView]);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        await AddUserAsync(admin, "Pending Person", NewEmail("pending"), StaffRole.Employee);

        var all = WebUtility.HtmlDecode(await admin.GetStringAsync(Users));
        Assert.Contains("Staff <span class=\"count\">4</span>", all);
        Assert.Contains("Invitation pending", all);
        Assert.Contains("Custom", all);
        Assert.Contains("Role defaults", all);
        Assert.True(all.IndexOf("admin@test.local", StringComparison.Ordinal) < all.IndexOf(manager.Email, StringComparison.Ordinal));
        Assert.True(all.IndexOf(manager.Email, StringComparison.Ordinal) < all.IndexOf(employee.Email, StringComparison.Ordinal));

        var managers = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}?role=manager"));
        Assert.Contains(manager.Email, managers);
        Assert.DoesNotContain(employee.Email, managers);

        var search = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Users}?q=Pending"));
        Assert.Contains("Pending Person", search);
        Assert.DoesNotContain(manager.Email, search);
    }

    // ---- Pinned access ----

    private static readonly string[] UserPageActions = ["Details", "Email", "Resend", "ReAdd", "Role", "Permissions", "Unlock", "Remove"];

    /// <summary>The agreed access of every Users and Permissions page and handler.</summary>
    private static readonly IReadOnlyDictionary<string, string> UsersAccess = new Dictionary<string, string>
    {
        ["/Pages/Admin/Users/Index.cshtml"] = Permissions.StaffView,
        ["/Pages/Admin/Users/Index.cshtml OnGetAsync"] = "page rule",
        ["/Pages/Admin/Users/Add.cshtml"] = "admin",
        ["/Pages/Admin/Users/Add.cshtml OnGet"] = "page rule",
        ["/Pages/Admin/Users/Add.cshtml OnPostAsync"] = "page rule",
        ["/Pages/Admin/Users/User.cshtml"] = Permissions.StaffView,
        ["/Pages/Admin/Users/User.cshtml OnGetAsync"] = "page rule",
        ["/Pages/Admin/Users/User.cshtml OnGetEmailStatusAsync"] = "admin",
        ["/Pages/Admin/Users/User.cshtml OnPostDetailsAsync"] = "admin",
        ["/Pages/Admin/Users/User.cshtml OnPostEmailAsync"] = "admin",
        ["/Pages/Admin/Users/User.cshtml OnPostResendAsync"] = "admin",
        ["/Pages/Admin/Users/User.cshtml OnPostReAddAsync"] = "admin",
        ["/Pages/Admin/Users/User.cshtml OnPostRoleAsync"] = "admin",
        ["/Pages/Admin/Users/User.cshtml OnPostPermissionsAsync"] = "admin",
        ["/Pages/Admin/Users/User.cshtml OnPostUnlockAsync"] = "admin",
        ["/Pages/Admin/Users/User.cshtml OnPostRemoveAsync"] = "admin",
        ["/Pages/Admin/Users/Permissions.cshtml"] = "admin",
        ["/Pages/Admin/Users/Permissions.cshtml OnGetAsync"] = "page rule",
        ["/Pages/Admin/Users/Permissions.cshtml OnPostSaveAsync"] = "page rule",
        ["/Pages/Admin/Users/Permissions.cshtml OnPostRestoreAsync"] = "page rule",
    };

    [Fact]
    public void TheStaffEmails_PlainText_HasNoGapBeforeAFullStop()
    {
        var renderer = servers.Get("Sqlite").Services.GetRequiredService<IEmailTemplateRenderer>();
        var model = new Dictionary<string, object?>
        {
            ["FirstName"] = "Eli",
            ["InvitedBy"] = "Site Administrator",
            ["RoleName"] = "Employee",
            ["InvitationUrl"] = "https://shutki.test/account/reset-password?invite=1",
            ["LoginUrl"] = "https://shutki.test/account/login",
        };

        foreach (var template in new[] { EmailTemplates.StaffInvitation, EmailTemplates.StaffAdded })
        {
            var text = renderer.Render(template, "Subject", model).Text;
            Assert.Contains("staff as Employee.", text);
            Assert.DoesNotContain(" .", text);
        }
    }

    /// <summary>Pinned in both directions: exactly these pages and handlers exist, each with exactly this rule.</summary>
    [Fact]
    public void TheUsersPages_HaveTheirAgreedAccess()
    {
        var pages = new RazorCompiledItemLoader().LoadItems(typeof(Program).Assembly)
            .Where(i => i.Kind == "mvc.1.0.razor-page" && i.Identifier.StartsWith("/Pages/Admin/Users/", StringComparison.Ordinal))
            .ToDictionary(i => i.Identifier, i => i.Type.GetProperty("Model")!.PropertyType);

        var actual = new Dictionary<string, string>();
        foreach (var (path, model) in pages)
        {
            actual[path] = Describe(PermissionPageFilter.RulesFor(model).Single());
            foreach (var handler in model.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.Name.StartsWith("On", StringComparison.Ordinal)))
            {
                var rules = PermissionPageFilter.RulesFor(handler);
                actual[$"{path} {handler.Name}"] = rules.Length == 0 ? "page rule" : Describe(rules.Single());
            }
        }

        Assert.Equal(UsersAccess.OrderBy(e => e.Key), actual.OrderBy(e => e.Key));

        static string Describe(Attribute rule) => rule switch
        {
            RequirePermissionAttribute r => r.Permission,
            AdminOnlyAttribute => "admin",
            AnyStaffAttribute => "any staff",
            var other => other.ToString()!,
        };
    }

    // ---- Helpers ----

    private static string NewEmail(string what) => $"{what}-{Guid.NewGuid():N}@test.local";

    /// <summary>
    /// The newest email to <paramref name="to"/> in the pickup folder; when none comes, says what the email log knows.
    /// While waiting it wakes the email dispatcher every second: on SQL Server an email the dispatcher looks for within the
    /// same 1/300 s as it was queued is not "due" yet (EmailOutboxRepository compares the datetime2 column with a datetime
    /// parameter), and is then only sent at the next 30-second poll.
    /// </summary>
    private static async Task<string> MailToAsync(AppFactory app, string to, DateTime? sinceUtc = null)
    {
        using var stop = new CancellationTokenSource();
        var signal = app.Services.GetRequiredService<EmailDispatchSignal>();
        var nudging = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stop.Token).ContinueWith(_ => { }, TaskScheduler.Default);
                signal.Notify();
            }
        });

        try
        {
            return await WaitForEmailAsync(app, to, MailWait, sinceUtc);
        }
        catch (Xunit.Sdk.XunitException ex)
        {
            await using var connection = await app.Services.GetRequiredService<IDbConnectionFactory>().OpenAsync();
            var rows = await connection.QueryAsync("SELECT Id, Subject, Status, Attempts, LastError FROM EmailOutbox WHERE ToAddresses = @To", new { To = to });
            throw new Xunit.Sdk.XunitException(
                $"{ex.Message} Email log: {string.Join(" | ", rows.Select(r => string.Join(", ", (IDictionary<string, object?>)r)))}.");
        }
        finally
        {
            await stop.CancelAsync();
            await nudging;
        }
    }

    private static async Task<HttpResponseMessage> AddUserAsync(
        HttpClient admin, string name, string email, StaffRole role, string? phone = null, string? jobTitle = null, string? confirmedUserId = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["Input.FullName"] = name,
            ["Input.Email"] = email,
            ["Input.PhoneNumber"] = phone ?? string.Empty,
            ["Input.JobTitle"] = jobTitle ?? string.Empty,
            ["Input.Role"] = role.ToString(),
        };
        if (confirmedUserId is not null)
        {
            fields["ConfirmedUserId"] = confirmedUserId;
        }

        return await PostFormAsync(admin, "/admin/users/add", await admin.GetStringAsync("/admin/users/add"), fields);
    }

    private static async Task<HttpResponseMessage> PostUserAsync(HttpClient client, string userId, string handler, Dictionary<string, string> fields) =>
        await PostFormAsync(client, $"{Users}/{userId}?handler={handler}", await client.GetStringAsync($"{Users}/{userId}"), fields);

    private static Dictionary<string, string> Levels(IReadOnlyDictionary<string, string> levels, string? mode, int? version)
    {
        var fields = levels.ToDictionary(l => $"levels[{l.Key}]", l => l.Value);
        if (mode is not null)
        {
            fields["mode"] = mode;
        }

        if (version is not null)
        {
            fields["version"] = version.Value.ToString();
        }

        return fields;
    }

    /// <summary>
    /// Opens the invitation link and sets the password on its form, as the invited person's browser would. Pass
    /// <paramref name="formHtml"/> to post a form opened earlier (e.g. before the link stopped working).
    /// </summary>
    private static async Task<HttpResponseMessage> AcceptInvitationAsync(HttpClient client, string link, string password, string? formHtml = null)
    {
        var url = PathAndQuery(link);
        var html = formHtml ?? await client.GetStringAsync(url);
        return await PostFormAsync(client, url, html, new Dictionary<string, string>
        {
            ["Input.Email"] = QueryValue(link, "email"),
            ["Input.Code"] = QueryValue(link, "code"),
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
            ["invite"] = "1",
        });
    }

    private static string InvitationLinkOnPage(string decodedHtml)
    {
        var match = InviteLinkInput().Match(decodedHtml);
        Assert.True(match.Success, "The page shows no invitation link.");
        return match.Groups[1].Value;
    }

    private static string InvitationLinkInEmail(string mail)
    {
        var match = InviteLinkHref().Match(mail);
        Assert.True(match.Success, "The email has no invitation link.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string QueryValue(string url, string key) => QueryHelpers.ParseQuery(new Uri(url).Query)[key].ToString();

    private static string PathAndQuery(string url) => new Uri(url).PathAndQuery;

    private static async Task<string> CreateAccountAsync(AppFactory app, string email, string fullName, string password, bool emailConfirmed)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = emailConfirmed, FullName = fullName, CreatedAtUtc = DateTime.UtcNow };
        var created = await users.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, Roles.Customer);
        return user.Id;
    }

    private static async Task<string> UserIdAsync(AppFactory app, string email) =>
        await FindUserIdAsync(app, email) ?? throw new Xunit.Sdk.XunitException($"No account for {email}.");

    private static async Task<string?> FindUserIdAsync(AppFactory app, string email)
    {
        using var scope = app.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))?.Id;
    }

    private static async Task<ApplicationUser> AccountAsync(AppFactory app, string userId)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId)
            ?? throw new Xunit.Sdk.XunitException($"No account {userId}.");
    }

    private static async Task<IReadOnlyList<string>> RoleNamesAsync(AppFactory app, string userId, bool staffOnly = false)
    {
        var roles = await QueryAsync<string>(app,
            "SELECT r.Name FROM UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId WHERE ur.UserId = @UserId ORDER BY r.Name", new { UserId = userId });
        return staffOnly ? roles.Where(r => r != Roles.Customer).ToList() : roles;
    }

    private static Task<StaffAccess> AccessAsync(AppFactory app, string userId) =>
        app.Services.GetRequiredService<IStaffAccessService>().GetAsync(userId);

    private static async Task<int> VersionAsync(AppFactory app, string userId)
    {
        using var scope = app.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IStaffPermissionStore>().GetUserPermissionsAsync(userId)).Version;
    }

    private static async Task<StaffMemberDto> MemberAsync(AppFactory app, string userId)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStaffDirectory>().GetAsync(userId)
            ?? throw new Xunit.Sdk.XunitException($"{userId} is not staff.");
    }

    private static async Task<IReadOnlyList<StaffAuditEntryDto>> AuditAsync(AppFactory app, string userId)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStaffAuditLog>().ListAsync(userId, 50);
    }

    private static async Task<DateTime?> OpenShiftStartAsync(AppFactory app, string userId)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITimeClockStatusReader>().GetOpenShiftStartAsync(userId);
    }

    private static async Task ExecuteAsync(AppFactory app, string sql, object? parameters = null)
    {
        await using var connection = await app.Services.GetRequiredService<IDbConnectionFactory>().OpenAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    private static async Task<IReadOnlyList<T>> QueryAsync<T>(AppFactory app, string sql, object? parameters = null)
    {
        await using var connection = await app.Services.GetRequiredService<IDbConnectionFactory>().OpenAsync();
        return (await connection.QueryAsync<T>(sql, parameters)).ToList();
    }

    [GeneratedRegex("<input id=\"invite-link\" class=\"input\" value=\"([^\"]+)\"")]
    private static partial Regex InviteLinkInput();

    [GeneratedRegex("href=\"(https://shutki\\.test/account/reset-password\\?invite=1[^\"]+)\"")]
    private static partial Regex InviteLinkHref();
}
