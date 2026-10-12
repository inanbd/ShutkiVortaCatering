using System.Net;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Application.Features.TimeClock;
using ShutkiVorta.Domain.TimeClock;
using ShutkiVorta.Infrastructure.Identity;
using ShutkiVorta.Infrastructure.Persistence;
using ShutkiVorta.IntegrationTests.Infrastructure;
using static ShutkiVorta.IntegrationTests.Infrastructure.StaffTestHelper;

namespace ShutkiVorta.IntegrationTests;

/// <summary>
/// Role changes, role-level permissions and the seeded admin. These change site-wide state, so each test runs its own app.
/// </summary>
public sealed class StaffRoleTests
{
    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task RoleChanges_KeepAtLeastOneAdmin_AndNobodyChangesTheirOwnRole(string provider)
    {
        await using var app = new AppFactory(provider);
        var seedAdmin = await UserIdAsync(app, AppFactory.AdminEmail);
        var manager = await CreateStaffAsync(app, StaffRole.Manager);

        // The only Admin cannot be demoted or removed, by anyone.
        var lastAdmin = await StoreAsync(app, s => s.SetStaffRoleAsync(seedAdmin, StaffRole.Manager, manager.UserId, "tests"));
        Assert.False(lastAdmin.Succeeded);
        Assert.Contains("at least one Admin", lastAdmin.Errors[0]);
        Assert.False((await StoreAsync(app, s => s.SetStaffRoleAsync(seedAdmin, null, manager.UserId, "tests"))).Succeeded);

        // An Admin who has not set a password yet does not count.
        var invited = await CreateStaffAsync(app, StaffRole.Admin);
        await ExecuteAsync(app, "UPDATE Users SET PasswordHash = NULL WHERE Id = @Id", new { Id = invited.UserId });
        Assert.False((await StoreAsync(app, s => s.SetStaffRoleAsync(seedAdmin, StaffRole.Employee, invited.UserId, "tests"))).Succeeded);

        // Nobody changes their own role, even with another Admin around.
        var secondAdmin = await CreateStaffAsync(app, StaffRole.Admin);
        var own = await StoreAsync(app, s => s.SetStaffRoleAsync(secondAdmin.UserId, StaffRole.Manager, secondAdmin.UserId, "tests"));
        Assert.False(own.Succeeded);
        Assert.Contains("your own role", own.Errors[0]);

        // ...not even with their id written differently (SQL Server compares ids case-insensitively and ignores trailing spaces).
        foreach (var disguised in new[] { secondAdmin.UserId.ToUpperInvariant(), secondAdmin.UserId + " " })
        {
            var sneaky = await StoreAsync(app, s => s.SetStaffRoleAsync(disguised, StaffRole.Employee, secondAdmin.UserId, "tests"));
            Assert.False(sneaky.Succeeded);
            Assert.True((await AccessAsync(app, secondAdmin.UserId)).IsAdmin);
        }

        // A locked-out Admin cannot sign in right now, so they do not count as the remaining Admin either.
        await ExecuteAsync(app, "UPDATE Users SET LockoutEnabled = 1, LockoutEndUtc = @Until WHERE Id = @Id",
            new { Id = secondAdmin.UserId, Until = DateTime.UtcNow.AddMinutes(15) });
        Assert.False((await StoreAsync(app, s => s.SetStaffRoleAsync(seedAdmin, StaffRole.Manager, secondAdmin.UserId, "tests"))).Succeeded);
        await ExecuteAsync(app, "UPDATE Users SET LockoutEndUtc = NULL WHERE Id = @Id", new { Id = secondAdmin.UserId });

        // With a second Admin who can sign in, the seeded one can be demoted (and then is a Manager on the next request).
        Assert.True((await StoreAsync(app, s => s.SetStaffRoleAsync(seedAdmin, StaffRole.Manager, secondAdmin.UserId, "tests"))).Succeeded);
        var access = await AccessAsync(app, seedAdmin);
        Assert.Equal(StaffRole.Manager, access.Role);
        Assert.False(access.IsAdmin);

        var audit = await AuditAsync(app, seedAdmin);
        Assert.Contains(audit, e => e.Action == StaffAuditActions.RoleChanged && e.Details == "Admin → Manager." && e.ChangedBy == "tests");
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task TwoAdminsDemotingEachOtherAtOnce_LeaveOneAdmin(string provider)
    {
        await using var app = new AppFactory(provider);
        var seedAdmin = await UserIdAsync(app, AppFactory.AdminEmail);
        var other = await CreateStaffAsync(app, StaffRole.Admin);

        var results = await Task.WhenAll(
            StoreAsync(app, s => s.SetStaffRoleAsync(seedAdmin, StaffRole.Employee, other.UserId, "other")),
            StoreAsync(app, s => s.SetStaffRoleAsync(other.UserId, StaffRole.Employee, seedAdmin, "seed")));

        Assert.Single(results, r => r.Succeeded);
        var admins = await ScalarAsync(app, "SELECT COUNT(*) FROM UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId WHERE r.NormalizedName = 'ADMIN'");
        Assert.Equal(1, admins);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task ChangingARole_ResetsCustomPermissions_AndLeavesOneStaffRole(string provider)
    {
        await using var app = new AppFactory(provider);
        var seedAdmin = await UserIdAsync(app, AppFactory.AdminEmail);
        var manager = await CreateStaffAsync(app, StaffRole.Manager, [Permissions.CustomersView, Permissions.EmailsManage]);
        Assert.True((await AccessAsync(app, manager.UserId)).Can(Permissions.EmailsManage));

        Assert.True((await StoreAsync(app, s => s.SetStaffRoleAsync(manager.UserId, StaffRole.Employee, seedAdmin, "tests"))).Succeeded);

        var access = await AccessAsync(app, manager.UserId);
        Assert.Equal(StaffRole.Employee, access.Role);
        Assert.False(access.UsesCustomPermissions);
        Assert.True(access.Permissions.SetEquals(PermissionCatalog.Defaults(StaffRole.Employee)));

        var custom = await StoreAsync(app, s => s.GetUserPermissionsAsync(manager.UserId));
        Assert.False(custom.UsesCustomPermissions);
        Assert.Empty(custom.Granted);
        Assert.Equal(1, await ScalarAsync(app, "SELECT COUNT(*) FROM UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId WHERE ur.UserId = @Id AND r.NormalizedName IN ('ADMIN', 'MANAGER', 'EMPLOYEE')", new { Id = manager.UserId }));
        Assert.Contains(await AuditAsync(app, manager.UserId), e => e.Details == "Manager → Employee. Their custom permissions were removed.");

        // Saving custom permissions with a stale version is refused.
        var stale = await StoreAsync(app, s => s.SetUserPermissionsAsync(manager.UserId, new HashSet<string> { Permissions.OrdersView }, custom.Version - 1, "tests"));
        Assert.False(stale.Succeeded);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task RemovingSomeone_ClosesTheirOpenShift_AndKeepsThemAsFormerStaff(string provider)
    {
        await using var app = new AppFactory(provider);
        var seedAdmin = await UserIdAsync(app, AppFactory.AdminEmail);
        var employee = await CreateStaffAsync(app, StaffRole.Employee);
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IStaffPermissionStore>().UpdateProfileAsync(employee.UserId, "Prep cook", "tests");
        }

        // A shift of today that is still going (not one forgotten on an earlier day), whatever time the test runs.
        var clock = app.Services.GetRequiredService<IDateTimeProvider>();
        var startOfToday = BusinessTime.StartOfDayUtc(BusinessTime.DateOf(clock.UtcNow, clock.BusinessTimeZone), clock.BusinessTimeZone);
        var clockIn = new[] { clock.UtcNow.AddHours(-3), startOfToday }.Max();
        await InsertOpenShiftAsync(app, employee.UserId, clockIn);

        using (var scope = app.Services.CreateScope())
        {
            var directory = scope.ServiceProvider.GetRequiredService<IStaffDirectory>();
            var member = await directory.GetAsync(employee.UserId);
            Assert.NotNull(member);
            Assert.Equal(StaffRole.Employee, member.Role);
            Assert.Equal("Prep cook", member.JobTitle);
            Assert.NotNull(member.OnClockSinceUtc);
            Assert.True(member.HasPassword);
            Assert.True(member.HasStaffAccess);
        }

        Assert.True((await StoreAsync(app, s => s.SetStaffRoleAsync(employee.UserId, null, seedAdmin, "tests"))).Succeeded);

        using (var scope = app.Services.CreateScope())
        {
            Assert.Null(await scope.ServiceProvider.GetRequiredService<ITimeClockStatusReader>().GetOpenShiftStartAsync(employee.UserId));
            var directory = scope.ServiceProvider.GetRequiredService<IStaffDirectory>();
            Assert.DoesNotContain(await directory.ListAsync(StaffListFilter.All), m => m.UserId == employee.UserId);
            var former = Assert.Single(await directory.ListFormerAsync(), m => m.UserId == employee.UserId);
            Assert.Null(former.Role);
            Assert.True(former.IsFormerStaff);
            Assert.Contains(await directory.ListAsync(new StaffListFilter(IncludeFormer: true)), m => m.UserId == employee.UserId);
        }

        var shift = await QuerySingleAsync<ShiftRow>(app, "SELECT ClockOutUtc, ClockOutSource, NeedsReview, EditReason FROM TimeClockShifts WHERE UserId = @Id", new { Id = employee.UserId });
        Assert.NotNull(shift.ClockOutUtc);
        Assert.InRange(shift.ClockOutUtc.Value, clock.UtcNow.AddMinutes(-1), clock.UtcNow); // closed now
        Assert.Equal(0, shift.ClockOutUtc.Value.Ticks % TimeSpan.TicksPerSecond); // to the second, like every punch
        Assert.Equal((int)ClockOutSource.System, shift.ClockOutSource);
        Assert.True(shift.NeedsReview);
        Assert.Equal("Closed when removed from staff", shift.EditReason);

        Assert.False((await AccessAsync(app, employee.UserId)).IsStaff);
        Assert.Contains(await AuditAsync(app, employee.UserId), e =>
            e.Action == StaffAuditActions.StaffRemoved && e.Details!.Contains("open shift was closed", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task RemovingSomeone_WhoForgotToClockOutDaysAgo_ClosesThatShiftAsAMissedClockOut_WithoutHours(string provider)
    {
        await using var app = new AppFactory(provider);
        var seedAdmin = await UserIdAsync(app, AppFactory.AdminEmail);
        var employee = await CreateStaffAsync(app, StaffRole.Employee);
        var clockIn = new DateTime(DateTime.UtcNow.AddDays(-3).Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        await InsertOpenShiftAsync(app, employee.UserId, clockIn);

        Assert.True((await StoreAsync(app, s => s.SetStaffRoleAsync(employee.UserId, null, seedAdmin, "tests"))).Succeeded);

        // Not 72 hours of pay and overtime: closed at its own clock-in, like "I don't remember", for a manager to fix.
        var row = await QuerySingleAsync<ShiftRow>(app, "SELECT ClockOutUtc, ClockOutSource, NeedsReview, EditReason FROM TimeClockShifts WHERE UserId = @Id", new { Id = employee.UserId });
        Assert.Equal(clockIn, DateTime.SpecifyKind(row.ClockOutUtc!.Value, DateTimeKind.Utc));
        Assert.Equal((int)ClockOutSource.Missed, row.ClockOutSource);
        Assert.True(row.NeedsReview);
        Assert.StartsWith("Missed clock-out", row.EditReason);

        using (var scope = app.Services.CreateScope())
        {
            var shifts = scope.ServiceProvider.GetRequiredService<ITimeClockRepository>();
            var shift = Assert.Single(await shifts.ListOverlappingAsync(clockIn.AddDays(-1), DateTime.UtcNow.AddDays(1), employee.UserId));
            Assert.True(shift.IsMissedClockOut);
            Assert.False(shift.CountsTowardsHours);
            Assert.Equal(TimeSpan.Zero, shift.HoursWithin(clockIn.AddDays(-7), DateTime.UtcNow.AddDays(1)));
        }

        Assert.Contains(await AuditAsync(app, employee.UserId), e =>
            e.Action == StaffAuditActions.StaffRemoved && e.Details!.Contains("closed as a missed clock-out", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task RolePermissions_ApplyOnTheNextRequest_AndStaleSavesAreRefused(string provider)
    {
        await using var app = new AppFactory(provider);
        var employee = await CreateStaffAsync(app, StaffRole.Employee);
        var client = await SignInAsync(app, employee.Email);
        AssertDenied(await client.GetAsync("/admin/inquiries"));

        var set = await StoreAsync(app, s => s.GetRolePermissionsAsync(StaffRole.Employee));
        Assert.Equal(0, set.Revision);
        Assert.True(set.IsDefault);

        var granted = new HashSet<string>(set.Granted) { Permissions.InquiriesView };
        granted.Remove(Permissions.DashboardView);
        Assert.True((await StoreAsync(app, s => s.SaveRolePermissionsAsync(StaffRole.Employee, granted, 0, "owner@test.local"))).Succeeded);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/inquiries")).StatusCode);
        Assert.Equal("/admin/time-clock", (await client.GetAsync("/admin")).Headers.Location!.OriginalString);

        var saved = await StoreAsync(app, s => s.GetRolePermissionsAsync(StaffRole.Employee));
        Assert.Equal(1, saved.Revision);
        Assert.False(saved.IsDefault);
        Assert.Equal("owner@test.local", saved.UpdatedBy);
        Assert.Equal(PermissionCatalog.AllPermissions.Count, saved.Overrides.Count);

        // A form loaded before that save is refused, not silently applied.
        var stale = await StoreAsync(app, s => s.SaveRolePermissionsAsync(StaffRole.Employee, PermissionCatalog.Defaults(StaffRole.Employee), 0, "someone"));
        Assert.False(stale.Succeeded);
        Assert.False((await StoreAsync(app, s => s.SaveRolePermissionsAsync(StaffRole.Admin, new HashSet<string>(), 0, "someone"))).Succeeded);

        // Managers are unaffected; restoring the defaults applies on the next request too.
        Assert.True((await StoreAsync(app, s => s.GetRolePermissionsAsync(StaffRole.Manager))).IsDefault);
        Assert.True((await StoreAsync(app, s => s.RestoreRoleDefaultsAsync(StaffRole.Employee, 1, "owner@test.local"))).Succeeded);
        AssertDenied(await client.GetAsync("/admin/inquiries"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin")).StatusCode);

        using var scope = app.Services.CreateScope();
        var entries = await scope.ServiceProvider.GetRequiredService<IStaffAuditLog>().ListByActionsAsync([StaffAuditActions.RoleDefaultsChanged], 10);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Details == "Employee permissions: Dashboard: Counts → No access; Inquiries: No access → View.");
    }

    /// <summary>
    /// The seeder must not undo a deliberate demotion on restart (SQLite only: restarting needs the same database file).
    /// It does restore the configured account when nobody holds the Admin role at all.
    /// </summary>
    [Fact]
    public async Task TheSeededAdmin_StaysDemotedAfterARestart_UnlessNoAdminIsLeft()
    {
        var directory = AppFactory.NewDirectory();
        try
        {
            string seedAdmin;
            await using (var app = new AppFactory("Sqlite", directory: directory))
            {
                seedAdmin = await UserIdAsync(app, AppFactory.AdminEmail);
                var owner = await CreateStaffAsync(app, StaffRole.Admin);
                Assert.True((await StoreAsync(app, s => s.SetStaffRoleAsync(seedAdmin, StaffRole.Manager, owner.UserId, "owner"))).Succeeded);
            }

            await using (var app = new AppFactory("Sqlite", directory: directory))
            {
                var access = await AccessAsync(app, seedAdmin);
                Assert.Equal(StaffRole.Manager, access.Role);
                Assert.False(access.IsAdmin);

                // Break-glass: with no Admin left at all, the next start makes the configured account an Admin again.
                await ExecuteAsync(app, "DELETE FROM UserRoles WHERE RoleId IN (SELECT Id FROM Roles WHERE NormalizedName = 'ADMIN')");
            }

            await using (var app = new AppFactory("Sqlite", directory: directory))
            {
                var access = await AccessAsync(app, seedAdmin);
                Assert.Equal(StaffRole.Admin, access.Role);
                Assert.Equal(1, await ScalarAsync(app,
                    "SELECT COUNT(*) FROM UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId WHERE ur.UserId = @Id AND r.NormalizedName IN ('ADMIN', 'MANAGER', 'EMPLOYEE')",
                    new { Id = seedAdmin }));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task ASecondOpenShift_IsAUniqueViolation(string provider)
    {
        await using var app = new AppFactory(provider);
        var employee = await CreateStaffAsync(app, StaffRole.Employee);
        await InsertOpenShiftAsync(app, employee.UserId, DateTime.UtcNow.AddHours(-1));

        var duplicate = await Assert.ThrowsAnyAsync<Exception>(() => InsertOpenShiftAsync(app, employee.UserId, DateTime.UtcNow));
        Assert.True(DbErrors.IsUniqueViolation(duplicate));

        var foreignKey = await Assert.ThrowsAnyAsync<Exception>(() => InsertOpenShiftAsync(app, "no-such-user", DateTime.UtcNow));
        Assert.False(DbErrors.IsUniqueViolation(foreignKey));

        using var scope = app.Services.CreateScope();
        var starts = await scope.ServiceProvider.GetRequiredService<ITimeClockStatusReader>().GetOpenShiftStartsAsync([employee.UserId, "nobody"]);
        Assert.Equal(DateTimeKind.Utc, Assert.Single(starts).Value.Kind);
    }

    /// <summary>The topbar and the dashboard show the signed-in person's open shift (via a replaced service: the AppFactory hook).</summary>
    [Fact]
    public async Task TheClockStatus_ShowsTheOpenShift()
    {
        var start = DateTime.UtcNow.AddHours(-3).AddMinutes(-12);
        await using var app = new AppFactory("Sqlite", configureServices: services =>
            services.AddScoped<ITimeClockStatusReader>(_ => new FixedShiftReader(start)));

        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var html = System.Net.WebUtility.HtmlDecode(await admin.GetStringAsync("/admin"));

        Assert.Contains("On the clock since", html);
        Assert.Contains("3 h 12 min", html);
        Assert.Contains("You're on the clock since", html);
        Assert.Contains(">Clock out</a>", html);
    }

    // ---- Helpers ----

    private static async Task<T> StoreAsync<T>(AppFactory app, Func<IStaffPermissionStore, Task<T>> action)
    {
        using var scope = app.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IStaffPermissionStore>());
    }

    private static Task<StaffAccess> AccessAsync(AppFactory app, string userId) =>
        app.Services.GetRequiredService<IStaffAccessService>().GetAsync(userId);

    private static async Task<IReadOnlyList<StaffAuditEntryDto>> AuditAsync(AppFactory app, string userId)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStaffAuditLog>().ListAsync(userId, 20);
    }

    private static async Task<string> UserIdAsync(AppFactory app, string email)
    {
        using var scope = app.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id;
    }

    private static Task InsertOpenShiftAsync(AppFactory app, string userId, DateTime clockInUtc) =>
        ExecuteAsync(app, """
            INSERT INTO TimeClockShifts (UserId, EmployeeName, ClockInUtc, ClockInLatitude, ClockInLongitude, ClockInAccuracyMeters,
                ClockInLocationStatus, CreatedAtUtc, UpdatedAtUtc)
            VALUES (@UserId, 'Test Employee', @ClockInUtc, 32.7767, -96.797, 20, @Status, @ClockInUtc, @ClockInUtc)
            """, new { UserId = userId, ClockInUtc = clockInUtc, Status = (int)LocationStatus.Captured });

    private static async Task ExecuteAsync(AppFactory app, string sql, object? parameters = null)
    {
        await using var connection = await app.Services.GetRequiredService<IDbConnectionFactory>().OpenAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    private static async Task<int> ScalarAsync(AppFactory app, string sql, object? parameters = null)
    {
        await using var connection = await app.Services.GetRequiredService<IDbConnectionFactory>().OpenAsync();
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    private static async Task<T> QuerySingleAsync<T>(AppFactory app, string sql, object? parameters = null)
    {
        await using var connection = await app.Services.GetRequiredService<IDbConnectionFactory>().OpenAsync();
        return await connection.QuerySingleAsync<T>(sql, parameters);
    }

    private sealed class ShiftRow
    {
        public DateTime? ClockOutUtc { get; init; }
        public int ClockOutSource { get; init; }
        public bool NeedsReview { get; init; }
        public string? EditReason { get; init; }
    }

    private sealed class FixedShiftReader(DateTime startUtc) : ITimeClockStatusReader
    {
        public Task<IReadOnlyDictionary<string, DateTime>> GetOpenShiftStartsAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, DateTime>>(userIds.ToDictionary(id => id, _ => startUtc));

        public Task<DateTime?> GetOpenShiftStartAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult<DateTime?>(startUtc);
    }
}
