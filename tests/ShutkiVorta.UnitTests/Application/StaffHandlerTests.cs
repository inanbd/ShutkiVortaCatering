using System.Reflection;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Application.Features.TimeClock;
using ShutkiVorta.UnitTests.TestDoubles;

namespace ShutkiVorta.UnitTests.Application;

/// <summary>Adding people and their invitations, roles and permissions: what the Users and Permissions pages send.</summary>
public sealed class StaffHandlerTests
{
    private const string AdminId = "admin-1";
    private const string Code = "SECRET-INVITATION-CODE";

    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IStaffDirectory _directory = Substitute.For<IStaffDirectory>();
    private readonly IStaffPermissionStore _store = Substitute.For<IStaffPermissionStore>();
    private readonly IStaffProfileVersions _versions = Substitute.For<IStaffProfileVersions>();
    private readonly IStaffAuditLog _audit = Substitute.For<IStaffAuditLog>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IEmailTemplateRenderer _renderer = Substitute.For<IEmailTemplateRenderer>();
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private readonly IStaffEmailLog _emailLog = Substitute.For<IStaffEmailLog>();
    private readonly ITimeClockStatusReader _timeClock = Substitute.For<ITimeClockStatusReader>();
    private readonly FakeUrls _urls = new();
    private readonly FakeCurrentUser _admin = new() { UserId = AdminId, Email = "owner@test.local", Name = "Owner Person" };
    private readonly FakeClock _clock = new(TestData.Now);

    private readonly List<StaffAuditEntry> _auditEntries = [];
    private readonly List<EmailMessage> _sent = [];

    public StaffHandlerTests()
    {
        _renderer.Render(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>>()).Returns(call =>
        {
            var model = call.ArgAt<IReadOnlyDictionary<string, object?>>(2);
            var body = string.Join(" ", model.Select(kv => $"{kv.Key}={kv.Value}"));
            return new RenderedEmail(call.ArgAt<string>(1), $"<p>{body}</p>", body);
        });
        _email.QueueAsync(Arg.Do<EmailMessage>(_sent.Add), Arg.Any<CancellationToken>()).Returns(ValueTask.CompletedTask);
        _audit.WriteAsync(Arg.Do<StaffAuditEntry>(_auditEntries.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _identity.GenerateStaffInvitationCodeAsync(Arg.Any<string>()).Returns(Code);
        _identity.CreateStaffAccountAsync(default!, default!, default).ReturnsForAnyArgs(Result<string>.Success("new-1"));
        _identity.ResetToInvitationAsync(default!).ReturnsForAnyArgs(Result.Success());
        _identity.RotateSecurityStampAsync(default!).ReturnsForAnyArgs(Result.Success());
        _identity.UpdateContactAsync(default!, default!, default).ReturnsForAnyArgs(Result.Success());
        _identity.ChangeEmailWithoutPasswordAsync(default!, default!).ReturnsForAnyArgs(Result.Success());
        _identity.UnlockAsync(default!).ReturnsForAnyArgs(Result.Success());
        _store.SetStaffRoleAsync(default!, default, default!, default!).ReturnsForAnyArgs(Result.Success());
        _store.SetUserPermissionsAsync(default!, default, default, default!).ReturnsForAnyArgs(Result.Success());
        _store.SaveRolePermissionsAsync(default, default!, default, default!).ReturnsForAnyArgs(Result.Success());
        _store.RestoreRoleDefaultsAsync(default, default, default!).ReturnsForAnyArgs(Result.Success());
        _store.GetUserPermissionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new CustomPermissionsDto { UserId = call.Arg<string>(), Version = 3 });
        _store.GetRolePermissionsAsync(Arg.Any<StaffRole>(), Arg.Any<CancellationToken>())
            .Returns(call => new RolePermissionSetDto { Role = call.Arg<StaffRole>(), Revision = 5, Granted = PermissionCatalog.Defaults(call.Arg<StaffRole>()) });
        _orders.CountByCustomerAsync(default!, default).ReturnsForAnyArgs(new Dictionary<string, int> { ["customer-1"] = 4 });

        // Every person's page showed version 3; another version means someone changed them meanwhile.
        _versions.ClaimAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<int>(1) == 3);
    }

    private StaffMemberCommandHandlers Members() =>
        new(_identity, _directory, _store, _versions, _audit, _orders, _renderer, _email, _urls, _admin, _clock);

    private StaffPermissionCommandHandlers PermissionCommands() => new(_directory, _store, _versions, _admin);

    private StaffQueryHandlers Queries() => new(_directory, _store, _audit, _emailLog, _timeClock);

    private static AddStaffMemberCommand Add(string email = "rahim@test.local", string? confirmedUserId = null, string? jobTitle = null) =>
        new("Rahim Uddin", email, "214-555-0101", StaffRole.Employee, jobTitle, confirmedUserId);

    private static UserAccount Account(string id, string email, bool confirmed, bool hasPassword) =>
        new() { Id = id, Email = email, FullName = "Account Owner", EmailConfirmed = confirmed, HasPassword = hasPassword, CreatedAtUtc = TestData.Now.AddYears(-1) };

    private static StaffMemberDto Member(string id, StaffRole? role, bool hasPassword = true, bool confirmed = true, bool locked = false) => new()
    {
        UserId = id,
        FullName = "Sumi Akter",
        Email = $"{id}@test.local",
        Role = role,
        HasPassword = hasPassword,
        EmailConfirmed = confirmed,
        IsLockedOut = locked,
    };

    private void Existing(UserAccount account, StaffMemberDto? member = null)
    {
        _identity.FindByEmailAsync(account.Email).Returns(account);
        _identity.FindByIdAsync(account.Id).Returns(account);
        _directory.GetAsync(account.Id, Arg.Any<CancellationToken>()).Returns(member);
    }

    private void Staff(StaffMemberDto member) => _directory.GetAsync(member.UserId, Arg.Any<CancellationToken>()).Returns(member);

    // ---- Adding people ----

    [Fact]
    public async Task AnEmailThatIsAlreadyStaff_IsRefused_AndNothingChanges()
    {
        Existing(Account("manager-1", "sumi@test.local", confirmed: true, hasPassword: true), Member("manager-1", StaffRole.Manager));

        var result = await Members().Handle(Add("sumi@test.local", confirmedUserId: "manager-1"), CancellationToken.None);

        Assert.Equal(AddStaffMemberOutcome.AlreadyStaff, result.Outcome);
        Assert.Equal(StaffRole.Manager, result.ExistingAccount!.Role);
        await _store.DidNotReceiveWithAnyArgs().SetStaffRoleAsync(default!, default, default!, default!);
        await _identity.DidNotReceiveWithAnyArgs().ResetToInvitationAsync(default!);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task AConfirmedAccount_IsShownForConfirmation_ThenAddedWithItsPassword_AndTold()
    {
        Existing(Account("customer-1", "nasrin@test.local", confirmed: true, hasPassword: true));

        var asked = await Members().Handle(Add("nasrin@test.local"), CancellationToken.None);
        Assert.Equal(AddStaffMemberOutcome.ConfirmExistingAccount, asked.Outcome);
        Assert.Equal(4, asked.ExistingAccount!.OrderCount);
        Assert.Equal("Account Owner", asked.ExistingAccount.FullName);
        await _store.DidNotReceiveWithAnyArgs().SetStaffRoleAsync(default!, default, default!, default!);

        // Confirming another account (e.g. the email changed hands meanwhile) asks again.
        Assert.Equal(AddStaffMemberOutcome.ConfirmExistingAccount, (await Members().Handle(Add("nasrin@test.local", confirmedUserId: "someone-else"), CancellationToken.None)).Outcome);

        var added = await Members().Handle(Add("nasrin@test.local", confirmedUserId: "customer-1", jobTitle: " Cook "), CancellationToken.None);

        Assert.Equal(AddStaffMemberOutcome.Added, added.Outcome);
        Assert.Equal(StaffInvitationKind.Added, added.Invitation!.Kind);
        Assert.Null(added.Invitation.Link);
        await _store.Received(1).SetStaffRoleAsync("customer-1", StaffRole.Employee, AdminId, "owner@test.local", Arg.Any<CancellationToken>());
        await _store.Received(1).UpdateProfileAsync("customer-1", "Cook", "owner@test.local", Arg.Any<CancellationToken>());
        await _identity.DidNotReceiveWithAnyArgs().ResetToInvitationAsync(default!);
        await _identity.DidNotReceiveWithAnyArgs().GenerateStaffInvitationCodeAsync(default!);
        var told = Assert.Single(_sent);
        Assert.Equal(StaffInvitations.AddedSubject, told.Subject);
        Assert.False(told.Sensitive);
    }

    [Fact]
    public async Task AnUnconfirmedAccount_IsTakenOver_BeforeItBecomesStaff_AndInvited()
    {
        Existing(Account("squatter-1", "rahim@test.local", confirmed: false, hasPassword: true));

        var result = await Members().Handle(Add(), CancellationToken.None);

        Assert.Equal(AddStaffMemberOutcome.Added, result.Outcome);
        Assert.Equal(StaffInvitationKind.Invited, result.Invitation!.Kind);
        Received.InOrder(() =>
        {
            _identity.ResetToInvitationAsync("squatter-1");
            _identity.UpdateContactAsync("squatter-1", "Rahim Uddin", "214-555-0101");
            _store.SetStaffRoleAsync("squatter-1", StaffRole.Employee, AdminId, "owner@test.local", Arg.Any<CancellationToken>());
            _identity.GenerateStaffInvitationCodeAsync("squatter-1");
        });
        var invitation = Assert.Single(_sent);
        Assert.True(invitation.Sensitive);
        Assert.Equal(StaffInvitations.InvitationSubject, invitation.Subject);
        Assert.Contains(_auditEntries, e => e.Action == StaffAuditActions.InvitationSent && e.Details!.Contains("never confirmed"));
    }

    [Fact]
    public async Task ANewPerson_GetsAnAccountWithoutAPassword_ARole_AndAnInvitation()
    {
        var result = await Members().Handle(Add(jobTitle: "Delivery"), CancellationToken.None);

        await _identity.Received(1).CreateStaffAccountAsync("Rahim Uddin", "rahim@test.local", "214-555-0101");
        await _store.Received(1).SetStaffRoleAsync("new-1", StaffRole.Employee, AdminId, "owner@test.local", Arg.Any<CancellationToken>());
        await _store.Received(1).UpdateProfileAsync("new-1", "Delivery", "owner@test.local", Arg.Any<CancellationToken>());
        var invitation = result.Invitation!;
        Assert.Equal("new-1", invitation.UserId);
        Assert.Equal(_urls.StaffInvitation("rahim@test.local", Code), invitation.Link);
        Assert.True(invitation.Emailed);
        Assert.Equal(TestData.Now + TimeSpan.FromDays(7), invitation.ExpiresAtUtc);
        Assert.Contains($"InvitationUrl={invitation.Link}", Assert.Single(_sent).HtmlBody);
    }

    [Fact]
    public async Task AnAccountThatCannotBeCreated_IsAValidationError()
    {
        _identity.CreateStaffAccountAsync(default!, default!, default).ReturnsForAnyArgs(Result<string>.Failure("Email 'x' is invalid."));

        var error = await Assert.ThrowsAsync<ValidationException>(() => Members().Handle(Add(), CancellationToken.None));

        Assert.Contains("Email", error.Errors.Keys);
        await _store.DidNotReceiveWithAnyArgs().SetStaffRoleAsync(default!, default, default!, default!);
    }

    [Fact]
    public async Task AnAccountAnotherRequestCreatedMeanwhile_IsNotTakenOver_NorInvitedTwice()
    {
        // Another tab created the account between this request's check and its insert, and has not made it staff yet.
        var raced = Account("raced-1", "rahim@test.local", confirmed: false, hasPassword: false);
        _identity.FindByEmailAsync("rahim@test.local").Returns(null, raced);
        _identity.CreateStaffAccountAsync(default!, default!, default).ReturnsForAnyArgs(Result<string>.Failure("Email 'rahim@test.local' is already taken."));

        var error = await Assert.ThrowsAsync<ValidationException>(() => Members().Handle(Add(), CancellationToken.None));

        Assert.Contains("was created a moment ago", Assert.Single(error.Errors["Email"]));
        await _identity.DidNotReceiveWithAnyArgs().ResetToInvitationAsync(default!);
        await _store.DidNotReceiveWithAnyArgs().SetStaffRoleAsync(default!, default, default!, default!);
        Assert.Empty(_sent);

        // Once that request has made them staff: "already on the staff", as for any repeated add.
        _identity.FindByEmailAsync("rahim@test.local").Returns(null, raced);
        _directory.GetAsync("raced-1", Arg.Any<CancellationToken>()).Returns(Member("raced-1", StaffRole.Employee, hasPassword: false, confirmed: false));

        var again = await Members().Handle(Add(), CancellationToken.None);

        Assert.Equal(AddStaffMemberOutcome.AlreadyStaff, again.Outcome);
        await _identity.DidNotReceiveWithAnyArgs().ResetToInvitationAsync(default!);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task TheInvitationLink_IsOnlyInTheSensitiveEmailAndTheResult_NeverInTheAuditLog()
    {
        var result = await Members().Handle(Add(), CancellationToken.None);

        Assert.Contains(Code, result.Invitation!.Link);
        Assert.True(Assert.Single(_sent).Sensitive);
        Assert.NotEmpty(_auditEntries);
        Assert.All(_auditEntries, e => Assert.DoesNotContain(Code, e.Details ?? string.Empty));
    }

    [Fact]
    public async Task WithoutAWebsiteAddress_TheInvitationIsNotEmailed_ButTheLinkIsShown()
    {
        _urls.HasConfiguredBaseUrl = false;

        var result = await Members().Handle(Add(), CancellationToken.None);

        Assert.False(result.Invitation!.Emailed);
        Assert.NotNull(result.Invitation.Link);
        Assert.Empty(_sent);
        Assert.Contains(_auditEntries, e => e.Action == StaffAuditActions.InvitationSent && e.Details!.Contains("Not emailed"));
    }

    [Fact]
    public async Task WithoutAWebsiteAddress_SomeoneWhoKeepsTheirPassword_IsNotEmailedEither()
    {
        _urls.HasConfiguredBaseUrl = false;
        Existing(Account("customer-1", "nasrin@test.local", confirmed: true, hasPassword: true));

        var added = await Members().Handle(Add("nasrin@test.local", confirmedUserId: "customer-1"), CancellationToken.None);

        Assert.Equal(StaffInvitationKind.Added, added.Invitation!.Kind);
        Assert.False(added.Invitation.Emailed);
        Assert.Empty(_sent);
        await _store.Received(1).SetStaffRoleAsync("customer-1", StaffRole.Employee, AdminId, "owner@test.local", Arg.Any<CancellationToken>());
    }

    // ---- Invitations again, email, details, unlock, removal ----

    [Fact]
    public async Task ResendingAnInvitation_EndsTheEarlierLinks_First()
    {
        Staff(Member("invited-1", StaffRole.Employee, hasPassword: false, confirmed: false));

        var result = await Members().Handle(new ResendStaffInvitationCommand("invited-1"), CancellationToken.None);

        Received.InOrder(() =>
        {
            _identity.ResetToInvitationAsync("invited-1");
            _identity.GenerateStaffInvitationCodeAsync("invited-1");
        });
        Assert.NotNull(result.Link);
        Assert.Single(_sent);
    }

    [Fact]
    public async Task ResendingToSomeoneWhoAlreadyGotIn_IsRefused()
    {
        Staff(Member("staff-1", StaffRole.Employee));

        var error = await Assert.ThrowsAsync<ValidationException>(() => Members().Handle(new ResendStaffInvitationCommand("staff-1"), CancellationToken.None));

        Assert.Contains("already set a password", error.Message);
        await _identity.DidNotReceiveWithAnyArgs().ResetToInvitationAsync(default!);
    }

    [Fact]
    public async Task TheEmail_CanOnlyBeChangedBeforeAPasswordIsSet_AndTheNewAddressIsInvited()
    {
        Staff(Member("staff-1", StaffRole.Employee));
        await Assert.ThrowsAsync<ValidationException>(() => Members().Handle(new ChangeStaffEmailCommand("staff-1", "new@test.local"), CancellationToken.None));
        await _identity.DidNotReceiveWithAnyArgs().ChangeEmailWithoutPasswordAsync(default!, default!);

        Staff(Member("invited-1", StaffRole.Employee, hasPassword: false, confirmed: false));
        var result = await Members().Handle(new ChangeStaffEmailCommand("invited-1", " fixed@test.local "), CancellationToken.None);

        await _identity.Received(1).ChangeEmailWithoutPasswordAsync("invited-1", "fixed@test.local");
        Assert.Equal("fixed@test.local", result.Email);
        Assert.Equal(["fixed@test.local"], Assert.Single(_sent).To);
        Assert.Contains(_auditEntries, e => e.Action == StaffAuditActions.EmailChanged && e.Details!.StartsWith("invited-1@test.local → fixed@test.local"));
    }

    [Fact]
    public async Task Details_AreSaved_WhenNobodyChangedThePersonMeanwhile()
    {
        Staff(Member("staff-1", StaffRole.Employee));

        await Assert.ThrowsAsync<ValidationException>(() =>
            Members().Handle(new UpdateStaffDetailsCommand("staff-1", "New Name", null, "Cook", Version: 2), CancellationToken.None));
        await _identity.DidNotReceiveWithAnyArgs().UpdateContactAsync(default!, default!, default);

        await Members().Handle(new UpdateStaffDetailsCommand("staff-1", " New Name ", " ", "Cook", Version: 3), CancellationToken.None);

        // The version is claimed first: the name and phone live on the account, so nothing else would move it on.
        Received.InOrder(() =>
        {
            _versions.ClaimAsync("staff-1", 3, "owner@test.local", Arg.Any<CancellationToken>());
            _identity.UpdateContactAsync("staff-1", "New Name", null);
        });
        await _store.Received(1).UpdateProfileAsync("staff-1", "Cook", "owner@test.local", Arg.Any<CancellationToken>());
        Assert.Contains(_auditEntries, e => e.Action == StaffAuditActions.ProfileChanged && e.Details!.Contains("Sumi Akter → New Name"));
    }

    [Fact]
    public async Task Unlocking_IsForLockedOutPeople_AndAudited()
    {
        Staff(Member("staff-1", StaffRole.Employee));
        await Assert.ThrowsAsync<ValidationException>(() => Members().Handle(new UnlockStaffSignInCommand("staff-1"), CancellationToken.None));

        Staff(Member("locked-1", StaffRole.Employee, locked: true));
        await Members().Handle(new UnlockStaffSignInCommand("locked-1"), CancellationToken.None);

        await _identity.Received(1).UnlockAsync("locked-1");
        Assert.Contains(_auditEntries, e => e.Action == StaffAuditActions.AccountUnlocked && e.TargetUserId == "locked-1");
    }

    [Fact]
    public async Task RemovingSomeone_GoesThroughTheStore_AndEndsAnUnusedInvitation()
    {
        Staff(Member("invited-1", StaffRole.Employee, hasPassword: false, confirmed: false));

        await Members().Handle(new RemoveFromStaffCommand("invited-1", Version: 3), CancellationToken.None);

        Received.InOrder(() =>
        {
            _versions.ClaimAsync("invited-1", 3, "owner@test.local", Arg.Any<CancellationToken>());
            _store.SetStaffRoleAsync("invited-1", null, AdminId, "owner@test.local", Arg.Any<CancellationToken>());
            _identity.RotateSecurityStampAsync("invited-1");
        });
    }

    [Fact]
    public async Task RemovingFromAStalePage_IsRefused()
    {
        Staff(Member("staff-1", StaffRole.Employee));

        var error = await Assert.ThrowsAsync<ValidationException>(() => Members().Handle(new RemoveFromStaffCommand("staff-1", Version: 1), CancellationToken.None));

        Assert.Equal(StaffVersions.ChangedMeanwhile, error.Message);
        await _store.DidNotReceiveWithAnyArgs().SetStaffRoleAsync(default!, default, default!, default!);
    }

    [Fact]
    public async Task ReAddingFormerStaff_InvitesThemOnlyIfTheyCannotSignIn()
    {
        Existing(Account("former-1", "former@test.local", confirmed: true, hasPassword: true), Member("former-1", null));
        var back = await Members().Handle(new ReAddStaffMemberCommand("former-1", StaffRole.Manager), CancellationToken.None);
        Assert.Equal(StaffInvitationKind.Added, back.Kind);
        await _identity.DidNotReceiveWithAnyArgs().ResetToInvitationAsync(default!);

        Existing(Account("former-2", "never@test.local", confirmed: false, hasPassword: false), Member("former-2", null, hasPassword: false, confirmed: false));
        var invited = await Members().Handle(new ReAddStaffMemberCommand("former-2", StaffRole.Employee), CancellationToken.None);
        Assert.Equal(StaffInvitationKind.Invited, invited.Kind);
        await _identity.Received(1).ResetToInvitationAsync("former-2");
        await _store.Received(1).SetStaffRoleAsync("former-2", StaffRole.Employee, AdminId, "owner@test.local", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAccountThatWasNeverStaff_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Members().Handle(new ResendStaffInvitationCommand("customer-1"), CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => PermissionCommands().Handle(new ChangeStaffRoleCommand("customer-1", StaffRole.Admin, 0), CancellationToken.None));
        Assert.Null(await Queries().Handle(new GetStaffMemberQuery("customer-1"), CancellationToken.None));
    }

    // ---- Roles and permissions ----

    [Fact]
    public async Task ChangingARole_GoesThroughTheStore_AndItsRefusalsBecomePageErrors()
    {
        Staff(Member("staff-1", StaffRole.Employee));
        _store.SetStaffRoleAsync("staff-1", StaffRole.Admin, AdminId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("There must always be at least one Admin who can sign in."));

        await PermissionCommands().Handle(new ChangeStaffRoleCommand("staff-1", StaffRole.Manager, 3), CancellationToken.None);
        Received.InOrder(() =>
        {
            _versions.ClaimAsync("staff-1", 3, "owner@test.local", Arg.Any<CancellationToken>());
            _store.SetStaffRoleAsync("staff-1", StaffRole.Manager, AdminId, "owner@test.local", Arg.Any<CancellationToken>());
        });

        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            PermissionCommands().Handle(new ChangeStaffRoleCommand("staff-1", StaffRole.Admin, 3), CancellationToken.None));
        Assert.Contains("at least one Admin", refused.Message);

        await Assert.ThrowsAsync<ValidationException>(() => PermissionCommands().Handle(new ChangeStaffRoleCommand("staff-1", StaffRole.Employee, 3), CancellationToken.None));

        // A form from a page that is out of date (its version was claimed by another change) never reaches the store.
        var stale = await Assert.ThrowsAsync<ValidationException>(() =>
            PermissionCommands().Handle(new ChangeStaffRoleCommand("staff-1", StaffRole.Manager, 2), CancellationToken.None));
        Assert.Equal(StaffVersions.ChangedMeanwhile, stale.Message);
        await _store.Received(1).SetStaffRoleAsync("staff-1", StaffRole.Manager, AdminId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CustomPermissions_AreTheChosenLevels_AndFollowingTheRoleClearsThem()
    {
        Staff(Member("staff-1", StaffRole.Employee));
        var levels = new Dictionary<string, string> { ["orders"] = "manage", ["dashboard"] = "sales", ["timeclock"] = "fix" };
        IReadOnlySet<string>? saved = null;
        await _store.SetUserPermissionsAsync("staff-1", Arg.Do<IReadOnlySet<string>?>(s => saved = s), 3, "owner@test.local", Arg.Any<CancellationToken>());

        Assert.True(await PermissionCommands().Handle(new SetStaffPermissionsCommand("staff-1", levels, 3), CancellationToken.None));

        // The modules not sent keep what the person has now (the Employee defaults).
        var expected = PermissionLevels.Grants(levels, PermissionCatalog.Defaults(StaffRole.Employee));
        Assert.True(saved!.SetEquals(expected));
        Assert.Contains(Permissions.OrdersManage, saved);
        Assert.Contains(Permissions.TimeClockViewAll, saved);
        Assert.Contains(Permissions.InventoryView, saved);

        _store.GetUserPermissionsAsync("staff-1", Arg.Any<CancellationToken>())
            .Returns(new CustomPermissionsDto { UserId = "staff-1", UsesCustomPermissions = true, Granted = expected, Version = 3 });
        Assert.True(await PermissionCommands().Handle(new SetStaffPermissionsCommand("staff-1", null, 3), CancellationToken.None));
        await _store.Received(1).SetUserPermissionsAsync("staff-1", null, 3, "owner@test.local", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SavingAPersonsPermissionsUnchanged_SavesNothing()
    {
        Staff(Member("staff-1", StaffRole.Employee));
        var employeeLevels = PermissionLevels.Of(PermissionCatalog.Defaults(StaffRole.Employee));

        // Following the role, and "follow the role" saved again.
        Assert.False(await PermissionCommands().Handle(new SetStaffPermissionsCommand("staff-1", null, 3), CancellationToken.None));

        // Custom permissions saved again with the same levels.
        var custom = new HashSet<string>(PermissionCatalog.Defaults(StaffRole.Employee)) { Permissions.OrdersView };
        _store.GetUserPermissionsAsync("staff-1", Arg.Any<CancellationToken>())
            .Returns(new CustomPermissionsDto { UserId = "staff-1", UsesCustomPermissions = true, Granted = custom, Version = 3 });
        Assert.False(await PermissionCommands().Handle(new SetStaffPermissionsCommand("staff-1", PermissionLevels.Of(custom), 3), CancellationToken.None));
        await _store.DidNotReceiveWithAnyArgs().SetUserPermissionsAsync(default!, default, default, default!);

        // Custom levels that happen to equal the role's still change something: the person no longer follows the role.
        _store.GetUserPermissionsAsync("staff-1", Arg.Any<CancellationToken>())
            .Returns(new CustomPermissionsDto { UserId = "staff-1", Version = 3 });
        Assert.True(await PermissionCommands().Handle(new SetStaffPermissionsCommand("staff-1", employeeLevels, 3), CancellationToken.None));
    }

    [Fact]
    public async Task AdminsHaveNoCustomPermissions()
    {
        Staff(Member("admin-2", StaffRole.Admin));

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            PermissionCommands().Handle(new SetStaffPermissionsCommand("admin-2", new Dictionary<string, string>(), 3), CancellationToken.None));

        Assert.Contains("every permission", error.Message);
    }

    [Fact]
    public async Task ARolesPermissions_AreSavedAtTheRevisionShown_AndRestoredToTheDefaults()
    {
        IReadOnlySet<string>? saved = null;
        await _store.SaveRolePermissionsAsync(StaffRole.Manager, Arg.Do<IReadOnlySet<string>>(s => saved = s), 5, "owner@test.local", Arg.Any<CancellationToken>());

        var noCustomers = new Dictionary<string, string> { ["customers"] = PermissionCatalog.NoAccess };
        Assert.True(await PermissionCommands().Handle(new SaveRolePermissionsCommand(StaffRole.Manager, noCustomers, 5), CancellationToken.None));

        var expected = new HashSet<string>(PermissionCatalog.Defaults(StaffRole.Manager));
        expected.Remove(Permissions.CustomersView);
        Assert.True(saved!.SetEquals(expected));

        _store.SaveRolePermissionsAsync(StaffRole.Manager, Arg.Any<IReadOnlySet<string>>(), 4, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Someone changed these permissions while you were editing them."));
        var stale = await Assert.ThrowsAsync<ValidationException>(() =>
            PermissionCommands().Handle(new SaveRolePermissionsCommand(StaffRole.Manager, noCustomers, 4), CancellationToken.None));
        Assert.Contains("Someone changed these permissions", stale.Message);

        // Saving the levels the role has already writes nothing: no new revision, no "No changes" in the recent changes.
        _store.ClearReceivedCalls();
        Assert.False(await PermissionCommands().Handle(
            new SaveRolePermissionsCommand(StaffRole.Manager, PermissionLevels.Of(PermissionCatalog.Defaults(StaffRole.Manager)), 5), CancellationToken.None));
        Assert.False(await PermissionCommands().Handle(new SaveRolePermissionsCommand(StaffRole.Manager, new Dictionary<string, string>(), 5), CancellationToken.None));
        await _store.DidNotReceiveWithAnyArgs().SaveRolePermissionsAsync(default, default!, default, default!);

        await PermissionCommands().Handle(new RestoreRoleDefaultsCommand(StaffRole.Employee, 7), CancellationToken.None);
        await _store.Received(1).RestoreRoleDefaultsAsync(StaffRole.Employee, 7, "owner@test.local", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void PermissionCommands_AcceptOnlyKnownLevels_AndConfigurableRoles()
    {
        Assert.True(new SaveRolePermissionsCommandValidator().Validate(new SaveRolePermissionsCommand(StaffRole.Employee, PermissionLevels.Of(new HashSet<string>()), 0)).IsValid);
        Assert.False(new SaveRolePermissionsCommandValidator().Validate(new SaveRolePermissionsCommand(StaffRole.Admin, new Dictionary<string, string>(), 0)).IsValid);
        Assert.False(new SaveRolePermissionsCommandValidator().Validate(new SaveRolePermissionsCommand(StaffRole.Employee, new Dictionary<string, string> { ["settings"] = "view" }, 0)).IsValid);
        Assert.False(new SetStaffPermissionsCommandValidator().Validate(new SetStaffPermissionsCommand("x", new Dictionary<string, string> { ["orders"] = "everything" }, 0)).IsValid);
        Assert.True(new SetStaffPermissionsCommandValidator().Validate(new SetStaffPermissionsCommand("x", null, 0)).IsValid);
        Assert.False(new AddStaffMemberCommandValidator().Validate(Add() with { Role = (StaffRole)9 }).IsValid);
        Assert.False(new AddStaffMemberCommandValidator().Validate(Add() with { PhoneNumber = "12" }).IsValid);
    }

    [Fact]
    public void PermissionLevels_RoundTripTheDefaults()
    {
        foreach (var role in StaffRoles.All)
        {
            var defaults = PermissionCatalog.Defaults(role);
            Assert.True(PermissionLevels.Grants(PermissionLevels.Of(defaults), new HashSet<string>()).SetEquals(defaults), role.ToString());
        }

        Assert.Equal("view", PermissionLevels.Of(new HashSet<string> { Permissions.OrdersView })["orders"]);
        Assert.Equal("own", PermissionLevels.Of(new HashSet<string>())["timeclock"]);
    }

    // ---- What the pages read ----

    [Fact]
    public async Task APersonsPage_ShowsTheirEffectivePermissions()
    {
        Staff(Member("custom-1", StaffRole.Employee));
        _store.GetUserPermissionsAsync("custom-1", Arg.Any<CancellationToken>())
            .Returns(new CustomPermissionsDto { UserId = "custom-1", UsesCustomPermissions = true, Granted = new HashSet<string> { Permissions.OrdersView }, Version = 8 });
        Staff(Member("role-1", StaffRole.Manager));
        Staff(Member("admin-2", StaffRole.Admin));
        _timeClock.GetOpenShiftStartAsync("role-1", Arg.Any<CancellationToken>()).Returns(TestData.Now.AddHours(-2));

        var custom = (await Queries().Handle(new GetStaffMemberQuery("custom-1"), CancellationToken.None))!;
        Assert.Equal(8, custom.Version);
        Assert.Equal([Permissions.OrdersView], custom.Permissions);
        Assert.True(custom.RolePermissions.SetEquals(PermissionCatalog.Defaults(StaffRole.Employee)));

        var follows = (await Queries().Handle(new GetStaffMemberQuery("role-1"), CancellationToken.None))!;
        Assert.True(follows.Permissions.SetEquals(PermissionCatalog.Defaults(StaffRole.Manager)));
        Assert.Equal(TestData.Now.AddHours(-2), follows.OnClockSinceUtc);

        var admin = (await Queries().Handle(new GetStaffMemberQuery("admin-2"), CancellationToken.None))!;
        Assert.True(admin.Permissions.SetEquals(PermissionCatalog.AllPermissions));
    }

    [Fact]
    public async Task TheStaffList_SplitsCurrentAndFormerStaff_AndFiltersByRole()
    {
        _directory.ListAsync(Arg.Any<StaffListFilter>(), Arg.Any<CancellationToken>()).Returns(
        [
            Member("a", StaffRole.Admin), Member("m", StaffRole.Manager), Member("e", StaffRole.Employee), Member("f", null),
        ]);

        var managers = await Queries().Handle(new GetStaffListQuery(StaffRole.Manager, " sumi "), CancellationToken.None);
        Assert.Equal(["m"], managers.Members.Select(m => m.UserId));
        Assert.Equal(3, managers.StaffCount);
        Assert.Equal(1, managers.FormerCount);
        await _directory.Received().ListAsync(new StaffListFilter(null, "sumi", true), Arg.Any<CancellationToken>());

        var former = await Queries().Handle(new GetStaffListQuery(StaffRole.Manager, null, Former: true), CancellationToken.None);
        Assert.Equal(["f"], former.Members.Select(m => m.UserId));
    }

    // ---- Who may send what ----

    [Fact]
    public void ChangingAnything_IsForAdmins_AndOnlyTheListQueriesNeedStaffView()
    {
        var requests = typeof(AddStaffMemberCommand).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(AddStaffMemberCommand).Namespace && t is { IsClass: true, IsAbstract: false } && typeof(IBaseRequest).IsAssignableFrom(t))
            .ToDictionary(t => t.Name, t => t.GetCustomAttribute<RequirePermissionAttribute>()?.Permission ?? (typeof(IRequireAdmin).IsAssignableFrom(t) ? "admin" : "none"));

        Assert.Equal(new Dictionary<string, string>
        {
            ["GetStaffListQuery"] = Permissions.StaffView,
            ["GetStaffMemberQuery"] = Permissions.StaffView,
            ["GetStaffEmailStatusQuery"] = "admin",
            ["GetPermissionsOverviewQuery"] = "admin",
            ["AddStaffMemberCommand"] = "admin",
            ["ReAddStaffMemberCommand"] = "admin",
            ["ResendStaffInvitationCommand"] = "admin",
            ["ChangeStaffEmailCommand"] = "admin",
            ["UpdateStaffDetailsCommand"] = "admin",
            ["UnlockStaffSignInCommand"] = "admin",
            ["RemoveFromStaffCommand"] = "admin",
            ["ChangeStaffRoleCommand"] = "admin",
            ["SetStaffPermissionsCommand"] = "admin",
            ["SaveRolePermissionsCommand"] = "admin",
            ["RestoreRoleDefaultsCommand"] = "admin",
        }.OrderBy(e => e.Key), requests.OrderBy(e => e.Key));
    }

    // ---- The invitation page ----

    [Fact]
    public async Task AnAcceptedInvitation_SignsThePersonIn_AndAPasswordResetDoesNot()
    {
        _identity.AcceptStaffInvitationAsync("rahim@test.local", Code, "Kitchen2026!").Returns(Result<string>.Success("new-1"));
        _identity.ResetPasswordAsync("rahim@test.local", Code, "Kitchen2026!").Returns(Result.Success());
        var accounts = new AccountCommandHandlers(_identity, _admin, _renderer, _email, _urls, NullLogger<AccountCommandHandlers>.Instance);

        var invited = await accounts.Handle(new ResetPasswordCommand(" rahim@test.local ", Code, "Kitchen2026!", IsStaffInvitation: true), CancellationToken.None);
        Assert.True(invited.Succeeded);
        Assert.Equal("new-1", invited.Value);
        await _identity.Received(1).SignInAsync("new-1", true);

        var reset = await accounts.Handle(new ResetPasswordCommand("rahim@test.local", Code, "Kitchen2026!"), CancellationToken.None);
        Assert.True(reset.Succeeded);
        Assert.Null(reset.Value);
        await _identity.Received(1).SignInAsync(Arg.Any<string>(), Arg.Any<bool>());

        _identity.AcceptStaffInvitationAsync("rahim@test.local", "old", "Kitchen2026!").Returns(Result<string>.Failure(StaffInvitations.ExpiredOrUsed));
        var expired = await accounts.Handle(new ResetPasswordCommand("rahim@test.local", "old", "Kitchen2026!", IsStaffInvitation: true), CancellationToken.None);
        Assert.Equal([StaffInvitations.ExpiredOrUsed], expired.Errors);
        await _identity.Received(1).SignInAsync(Arg.Any<string>(), Arg.Any<bool>());
    }
}
