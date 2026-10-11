using System.Data.Common;
using Dapper;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Domain.TimeClock;

namespace ShutkiVorta.Infrastructure.Persistence.Repositories;

/// <summary>
/// The staff list and every change to who is staff and what they may do. Each change runs in one transaction that first
/// locks the ADMIN row of Roles, so role changes and permission saves are serialised on both providers (SQLite already
/// writes one transaction at a time; on SQL Server the row lock is held until commit, also under snapshot isolation).
/// </summary>
internal sealed class StaffRepository(IDbConnectionFactory connections, IDateTimeProvider clock) : IStaffDirectory, IStaffPermissionStore
{
    private const string StaffRoleNames = "'ADMIN', 'MANAGER', 'EMPLOYEE'";

    private const string StalePermissions = "Someone changed these permissions while you were editing them. Reload the page to see the latest and try again.";
    private const string StaleProfile = "Someone changed this person's permissions while you were editing them. Reload the page to see the latest and try again.";

    // One row per person even if the data holds several staff roles (the lowest, i.e. the highest rank number, counts);
    // the unique open-shift index means at most one open shift joins.
    private const string MemberSelect = $"""
        SELECT u.Id AS UserId, u.FullName, u.Email, u.PhoneNumber, u.EmailConfirmed, u.CreatedAtUtc, u.LockoutEnabled, u.LockoutEndUtc,
               CASE WHEN u.PasswordHash IS NULL THEN 0 ELSE 1 END AS HasPassword,
               sr.StaffRank, sr.RoleCount, sp.UserId AS ProfileUserId, sp.JobTitle, sp.UsesCustomPermissions,
               s.ClockInUtc AS OnClockSinceUtc
        FROM Users u
        LEFT JOIN (SELECT ur.UserId,
                          MAX(CASE r.NormalizedName WHEN 'ADMIN' THEN 1 WHEN 'MANAGER' THEN 2 ELSE 3 END) AS StaffRank,
                          COUNT(*) AS RoleCount
                     FROM UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId
                    WHERE r.NormalizedName IN ({StaffRoleNames})
                    GROUP BY ur.UserId) sr ON sr.UserId = u.Id
        LEFT JOIN StaffProfiles sp ON sp.UserId = u.Id
        LEFT JOIN TimeClockShifts s ON s.UserId = u.Id AND s.ClockOutUtc IS NULL
        """;

    private const string MemberOrder = "ORDER BY CASE WHEN sr.StaffRank IS NULL THEN 4 ELSE sr.StaffRank END, UPPER(u.FullName), u.Id";

    // ---- IStaffDirectory ----

    public async Task<IReadOnlyList<StaffMemberDto>> ListAsync(StaffListFilter filter, CancellationToken cancellationToken = default)
    {
        var conditions = new List<string> { filter.IncludeFormer ? "(sr.UserId IS NOT NULL OR sp.UserId IS NOT NULL)" : "sr.UserId IS NOT NULL" };
        var parameters = new DynamicParameters();
        if (filter.Role is { } role)
        {
            conditions.Add("sr.StaffRank = @Rank");
            parameters.Add("Rank", (int)role);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Replace("%", string.Empty).Replace("_", string.Empty).Replace("[", string.Empty).Trim();
            conditions.Add("(u.FullName LIKE @Pattern OR u.Email LIKE @Pattern OR u.PhoneNumber LIKE @Pattern)");
            parameters.Add("Pattern", $"%{term}%");
        }

        return await QueryMembersAsync($"{MemberSelect} WHERE {string.Join(" AND ", conditions)} {MemberOrder}", parameters, cancellationToken);
    }

    public async Task<StaffMemberDto?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        var members = await QueryMembersAsync(
            $"{MemberSelect} WHERE u.Id = @UserId AND (sr.UserId IS NOT NULL OR sp.UserId IS NOT NULL)", new { UserId = userId }, cancellationToken);
        return members.Count == 0 ? null : members[0];
    }

    public Task<IReadOnlyList<StaffMemberDto>> ListFormerAsync(CancellationToken cancellationToken = default) =>
        QueryMembersAsync($"{MemberSelect} WHERE sr.UserId IS NULL AND sp.UserId IS NOT NULL {MemberOrder}", null, cancellationToken);

    private async Task<IReadOnlyList<StaffMemberDto>> QueryMembersAsync(string sql, object? parameters, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<MemberRow>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var now = clock.UtcNow;
        return rows.Select(r => r.ToDto(now)).ToList();
    }

    // ---- Role permissions ----

    public async Task<RolePermissionSetDto> GetRolePermissionsAsync(StaffRole role, CancellationToken cancellationToken = default)
    {
        if (role == StaffRole.Admin)
        {
            return new RolePermissionSetDto { Role = role, Granted = PermissionCatalog.Defaults(role) };
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        var set = await connection.QuerySingleOrDefaultAsync<RoleSetRow>(new CommandDefinition(
            "SELECT Revision, UpdatedAtUtc, UpdatedBy FROM RolePermissionSets WHERE Role = @Role", new { Role = role.RoleName() }, cancellationToken: cancellationToken));
        var overrides = await ReadRoleOverridesAsync(connection, null, role, cancellationToken);

        return new RolePermissionSetDto
        {
            Role = role,
            Revision = set?.Revision ?? 0,
            Overrides = overrides,
            Granted = PermissionCatalog.ForRole(role, overrides),
            UpdatedAtUtc = set is null ? null : DateTime.SpecifyKind(set.UpdatedAtUtc, DateTimeKind.Utc),
            UpdatedBy = set?.UpdatedBy,
        };
    }

    public Task<Result> SaveRolePermissionsAsync(
        StaffRole role, IReadOnlySet<string> granted, int expectedRevision, string changedBy, CancellationToken cancellationToken = default) =>
        ChangeRolePermissionsAsync(role, PermissionCatalog.Normalize(granted), expectedRevision, changedBy, cancellationToken);

    public Task<Result> RestoreRoleDefaultsAsync(StaffRole role, int expectedRevision, string changedBy, CancellationToken cancellationToken = default) =>
        ChangeRolePermissionsAsync(role, null, expectedRevision, changedBy, cancellationToken);

    /// <param name="granted">The full new set, or null to remove the admin's changes (back to the catalog defaults).</param>
    private async Task<Result> ChangeRolePermissionsAsync(
        StaffRole role, IReadOnlySet<string>? granted, int expectedRevision, string changedBy, CancellationToken cancellationToken)
    {
        if (role == StaffRole.Admin)
        {
            return Result.Failure("Admins always have every permission.");
        }

        var now = clock.UtcNow;
        var roleName = role.RoleName();

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockStaffChangesAsync(connection, transaction, cancellationToken);

        if (!await BumpRevisionAsync(connection, transaction, roleName, expectedRevision, changedBy, now, cancellationToken))
        {
            return Result.Failure(StalePermissions);
        }

        var before = PermissionCatalog.ForRole(role, await ReadRoleOverridesAsync(connection, transaction, role, cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM RolePermissions WHERE Role = @Role", new { Role = roleName }, transaction, cancellationToken: cancellationToken));

        // Every permission the catalog knows is stored, so the saved choice holds even if a default changes later;
        // a permission added to the catalog later has no row and starts at its default.
        if (granted is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO RolePermissions (Role, Permission, IsGranted) VALUES (@Role, @Permission, @IsGranted)",
                PermissionCatalog.AllPermissions.Select(p => new { Role = roleName, Permission = p, IsGranted = granted.Contains(p) }).ToList(),
                transaction,
                cancellationToken: cancellationToken));
        }

        var after = granted ?? PermissionCatalog.Defaults(role);
        var changes = PermissionCatalog.DescribeChanges(before, after) ?? "No changes";
        await StaffAuditLogRepository.InsertAsync(connection, transaction, new StaffAuditEntry(
            null, null, StaffAuditActions.RoleDefaultsChanged,
            granted is null ? $"{roleName} permissions restored to the defaults. {changes}." : $"{roleName} permissions: {changes}.",
            changedBy), now, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Optimistic concurrency for a role's permissions: true when the revision matched and was incremented.</summary>
    private static async Task<bool> BumpRevisionAsync(
        DbConnection connection, DbTransaction transaction, string roleName, int expectedRevision, string changedBy, DateTime now, CancellationToken cancellationToken)
    {
        var parameters = new { Role = roleName, Expected = expectedRevision, UpdatedAtUtc = now, UpdatedBy = changedBy };
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE RolePermissionSets SET Revision = Revision + 1, UpdatedAtUtc = @UpdatedAtUtc, UpdatedBy = @UpdatedBy
            WHERE Role = @Role AND Revision = @Expected
            """, parameters, transaction, cancellationToken: cancellationToken));
        if (updated == 1)
        {
            return true;
        }

        // No row yet means the role has never been saved (revision 0). The lock rules out a concurrent insert.
        var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM RolePermissionSets WHERE Role = @Role", parameters, transaction, cancellationToken: cancellationToken));
        if (exists > 0 || expectedRevision != 0)
        {
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO RolePermissionSets (Role, Revision, UpdatedAtUtc, UpdatedBy) VALUES (@Role, 1, @UpdatedAtUtc, @UpdatedBy)",
            parameters, transaction, cancellationToken: cancellationToken));
        return true;
    }

    private static async Task<IReadOnlyDictionary<string, bool>> ReadRoleOverridesAsync(
        DbConnection connection, DbTransaction? transaction, StaffRole role, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<PermissionRow>(new CommandDefinition(
            "SELECT Permission, IsGranted FROM RolePermissions WHERE Role = @Role", new { Role = role.RoleName() }, transaction, cancellationToken: cancellationToken));
        return rows.ToDictionary(r => r.Permission, r => r.IsGranted, StringComparer.Ordinal);
    }

    // ---- Per-person permissions ----

    public async Task<CustomPermissionsDto> GetUserPermissionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var profile = await ReadProfileAsync(connection, null, userId, cancellationToken);
        var grants = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT Permission FROM UserPermissions WHERE UserId = @UserId", new { UserId = userId }, cancellationToken: cancellationToken));

        return new CustomPermissionsDto
        {
            UserId = userId,
            UsesCustomPermissions = profile?.UsesCustomPermissions ?? false,
            Granted = PermissionCatalog.Normalize(grants),
            Version = profile?.Version ?? 0,
        };
    }

    public async Task<Result> SetUserPermissionsAsync(
        string userId, IReadOnlySet<string>? granted, int expectedVersion, string changedBy, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var custom = granted is null ? null : PermissionCatalog.Normalize(granted);

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockStaffChangesAsync(connection, transaction, cancellationToken);

        var user = await ReadUserAsync(connection, transaction, userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure("This account no longer exists.");
        }

        userId = user.Id; // the id exactly as stored (see SetStaffRoleAsync)
        var role = StaffRoles.Lowest(await ReadStaffRolesAsync(connection, transaction, userId, cancellationToken));
        if (role is not { } staffRole)
        {
            return Result.Failure($"{user.FullName} is not on the staff.");
        }

        if (staffRole == StaffRole.Admin)
        {
            return Result.Failure("Admins always have every permission.");
        }

        var profile = await ReadProfileAsync(connection, transaction, userId, cancellationToken);
        if (profile is null)
        {
            await InsertProfileAsync(connection, transaction, userId, null, changedBy, now, cancellationToken);
        }

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE StaffProfiles SET UsesCustomPermissions = @Custom, Version = Version + 1, UpdatedAtUtc = @UpdatedAtUtc, UpdatedBy = @UpdatedBy
            WHERE UserId = @UserId AND Version = @Expected
            """,
            new { UserId = userId, Custom = custom is not null, Expected = expectedVersion, UpdatedAtUtc = now, UpdatedBy = changedBy },
            transaction,
            cancellationToken: cancellationToken));
        if (updated == 0)
        {
            return Result.Failure(StaleProfile);
        }

        var roleSet = PermissionCatalog.ForRole(staffRole, await ReadRoleOverridesAsync(connection, transaction, staffRole, cancellationToken));
        var before = profile?.UsesCustomPermissions == true
            ? PermissionCatalog.Normalize(await connection.QueryAsync<string>(new CommandDefinition(
                "SELECT Permission FROM UserPermissions WHERE UserId = @UserId", new { UserId = userId }, transaction, cancellationToken: cancellationToken)))
            : roleSet;

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM UserPermissions WHERE UserId = @UserId", new { UserId = userId }, transaction, cancellationToken: cancellationToken));
        if (custom is { Count: > 0 })
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO UserPermissions (UserId, Permission) VALUES (@UserId, @Permission)",
                custom.Select(p => new { UserId = userId, Permission = p }).ToList(),
                transaction,
                cancellationToken: cancellationToken));
        }

        var changes = PermissionCatalog.DescribeChanges(before, custom ?? roleSet) ?? "No changes";
        await StaffAuditLogRepository.InsertAsync(connection, transaction, new StaffAuditEntry(
            userId, user.FullName, StaffAuditActions.PermissionsChanged,
            custom is null ? $"Follows the {staffRole.RoleName()} permissions. {changes}." : $"Custom permissions. {changes}.",
            changedBy), now, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    // ---- Roles ----

    public async Task<Result> SetStaffRoleAsync(string userId, StaffRole? role, string actorUserId, string changedBy, CancellationToken cancellationToken = default)
    {
        if (IsSameUser(userId, actorUserId))
        {
            return Result.Failure("You can't change your own role or remove yourself from the staff. Ask another admin.");
        }

        var now = clock.UtcNow;
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockStaffChangesAsync(connection, transaction, cancellationToken);

        var user = await ReadUserAsync(connection, transaction, userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure("This account no longer exists.");
        }

        // From here on use the id exactly as stored: SQL Server matches ids case-insensitively and ignores trailing
        // spaces, so "the same person written differently" must not get past the own-role rule.
        userId = user.Id;
        if (IsSameUser(userId, actorUserId))
        {
            return Result.Failure("You can't change your own role or remove yourself from the staff. Ask another admin.");
        }

        var currentRoles = await ReadStaffRolesAsync(connection, transaction, userId, cancellationToken);
        var current = StaffRoles.Lowest(currentRoles);
        var profile = await ReadProfileAsync(connection, transaction, userId, cancellationToken);

        if (current == role && currentRoles.Count <= 1)
        {
            // Nothing to change; staff who joined before profiles existed (e.g. the first admin) get one now.
            if (role is not null && profile is null)
            {
                await InsertProfileAsync(connection, transaction, userId, null, changedBy, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            return Result.Success();
        }

        if (current == StaffRole.Admin && role != StaffRole.Admin && await CountOtherActiveAdminsAsync(connection, transaction, userId, now, cancellationToken) == 0)
        {
            return Result.Failure(
                "There must always be at least one Admin who can sign in. Make someone else an Admin (and let them set their password) first.");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM UserRoles WHERE UserId = @UserId AND RoleId IN (SELECT Id FROM Roles WHERE NormalizedName IN ({StaffRoleNames}))",
            new { UserId = userId }, transaction, cancellationToken: cancellationToken));

        if (role is { } newRole)
        {
            var inserted = await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO UserRoles (UserId, RoleId) SELECT @UserId, Id FROM Roles WHERE NormalizedName = @Role",
                new { UserId = userId, Role = newRole.NormalizedName() }, transaction, cancellationToken: cancellationToken));
            if (inserted == 0)
            {
                throw new InvalidOperationException($"The {newRole.RoleName()} role does not exist; it is created when the site starts.");
            }
        }

        // A new role (or leaving) always starts from the role's permissions: old custom grants must not linger.
        var roleChanged = current != role;
        var hadCustomPermissions = roleChanged && profile?.UsesCustomPermissions == true;
        if (profile is null)
        {
            await InsertProfileAsync(connection, transaction, userId, null, changedBy, now, cancellationToken);
        }
        else
        {
            await connection.ExecuteAsync(new CommandDefinition(
                roleChanged
                    ? "UPDATE StaffProfiles SET UsesCustomPermissions = 0, Version = Version + 1, UpdatedAtUtc = @UpdatedAtUtc, UpdatedBy = @UpdatedBy WHERE UserId = @UserId"
                    : "UPDATE StaffProfiles SET Version = Version + 1, UpdatedAtUtc = @UpdatedAtUtc, UpdatedBy = @UpdatedBy WHERE UserId = @UserId",
                new { UserId = userId, UpdatedAtUtc = now, UpdatedBy = changedBy }, transaction, cancellationToken: cancellationToken));
        }

        if (roleChanged)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM UserPermissions WHERE UserId = @UserId", new { UserId = userId }, transaction, cancellationToken: cancellationToken));
        }

        var closedShift = role is null && await CloseOpenShiftAsync(connection, transaction, userId, changedBy, now, cancellationToken);

        var (action, details) = Describe(current, currentRoles, role, hadCustomPermissions, closedShift);
        await StaffAuditLogRepository.InsertAsync(connection, transaction, new StaffAuditEntry(userId, user.FullName, action, details, changedBy), now, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    public async Task UpdateProfileAsync(string userId, string? jobTitle, string changedBy, CancellationToken cancellationToken = default)
    {
        var title = string.IsNullOrWhiteSpace(jobTitle) ? null : jobTitle.Trim();
        title = title is { Length: > 100 } ? title[..100] : title;
        var now = clock.UtcNow;

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var user = await ReadUserAsync(connection, transaction, userId, cancellationToken);
        if (user is null)
        {
            return;
        }

        userId = user.Id; // the id exactly as stored (see SetStaffRoleAsync)
        var profile = await ReadProfileAsync(connection, transaction, userId, cancellationToken);
        if (profile is null)
        {
            await InsertProfileAsync(connection, transaction, userId, title, changedBy, now, cancellationToken);
        }
        else if (profile.JobTitle != title)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE StaffProfiles SET JobTitle = @JobTitle, Version = Version + 1, UpdatedAtUtc = @UpdatedAtUtc, UpdatedBy = @UpdatedBy WHERE UserId = @UserId",
                new { UserId = userId, JobTitle = title, UpdatedAtUtc = now, UpdatedBy = changedBy }, transaction, cancellationToken: cancellationToken));
        }

        if (profile?.JobTitle != title)
        {
            await StaffAuditLogRepository.InsertAsync(connection, transaction, new StaffAuditEntry(
                userId, user.FullName, StaffAuditActions.ProfileChanged,
                title is null ? "Job title removed." : $"Job title: {title}.", changedBy), now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // ---- Helpers ----

    /// <summary>Ids are GUID strings; compare them the way SQL Server's default collation would (case and trailing spaces).</summary>
    private static bool IsSameUser(string userId, string actorUserId) =>
        string.Equals(userId.Trim(), actorUserId.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Serialises staff changes: a no-op update takes the ADMIN row's write lock until the transaction ends, so a
    /// concurrent change waits and then sees this one's result (e.g. two admins demoting each other).
    /// </summary>
    private static Task LockStaffChangesAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            "UPDATE Roles SET ConcurrencyStamp = ConcurrencyStamp WHERE NormalizedName = 'ADMIN'", transaction: transaction, cancellationToken: cancellationToken));

    /// <summary>
    /// Admins other than this person who are only Admin and can sign in now (a password, a confirmed email, not locked out).
    /// </summary>
    private static Task<int> CountOtherActiveAdminsAsync(
        DbConnection connection, DbTransaction transaction, string userId, DateTime now, CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM Users u
             WHERE u.Id <> @UserId AND u.PasswordHash IS NOT NULL AND u.EmailConfirmed = 1
               AND (u.LockoutEnabled = 0 OR u.LockoutEndUtc IS NULL OR u.LockoutEndUtc <= @Now)
               AND EXISTS (SELECT 1 FROM UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId
                            WHERE ur.UserId = u.Id AND r.NormalizedName = 'ADMIN')
               AND NOT EXISTS (SELECT 1 FROM UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId
                                WHERE ur.UserId = u.Id AND r.NormalizedName IN ('MANAGER', 'EMPLOYEE'))
            """, new { UserId = userId, Now = now }, transaction, cancellationToken: cancellationToken));

    /// <summary>Closes the person's open shift now, flagged for review. True when there was one.</summary>
    private static async Task<bool> CloseOpenShiftAsync(
        DbConnection connection, DbTransaction transaction, string userId, string changedBy, DateTime now, CancellationToken cancellationToken)
    {
        var closed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE TimeClockShifts
               SET ClockOutUtc = CASE WHEN ClockInUtc > @Now THEN ClockInUtc ELSE @Now END,
                   ClockOutSource = @Source, NeedsReview = 1, EditedBy = @EditedBy, EditedAtUtc = @Now,
                   EditReason = 'Closed when removed from staff', Version = Version + 1, UpdatedAtUtc = @Now
             WHERE UserId = @UserId AND ClockOutUtc IS NULL
            """,
            new { UserId = userId, Now = now, Source = (int)ClockOutSource.System, EditedBy = changedBy },
            transaction,
            cancellationToken: cancellationToken));
        return closed > 0;
    }

    private static (string Action, string Details) Describe(
        StaffRole? current, IReadOnlyCollection<StaffRole> currentRoles, StaffRole? role, bool hadCustomPermissions, bool closedShift)
    {
        var reset = hadCustomPermissions ? " Their custom permissions were removed." : string.Empty;
        if (role is null)
        {
            return (StaffAuditActions.StaffRemoved,
                $"Removed from the staff (was {current?.RoleName()})." + reset +
                (closedShift ? " Their open shift was closed and flagged for review." : string.Empty));
        }

        if (current is null)
        {
            return (StaffAuditActions.StaffAdded, $"Added to the staff as {role.Value.RoleName()}.");
        }

        if (currentRoles.Count > 1)
        {
            return (StaffAuditActions.RoleChanged,
                $"Several staff roles ({string.Join(", ", currentRoles.Order().Select(r => r.RoleName()))}) replaced by {role.Value.RoleName()}." + reset);
        }

        return (StaffAuditActions.RoleChanged, $"{current.Value.RoleName()} → {role.Value.RoleName()}." + reset);
    }

    private static async Task<IReadOnlyCollection<StaffRole>> ReadStaffRolesAsync(
        DbConnection connection, DbTransaction? transaction, string userId, CancellationToken cancellationToken)
    {
        var names = await connection.QueryAsync<string>(new CommandDefinition(
            $"""
            SELECT r.NormalizedName FROM UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId
             WHERE ur.UserId = @UserId AND r.NormalizedName IN ({StaffRoleNames})
            """, new { UserId = userId }, transaction, cancellationToken: cancellationToken));
        return names.Select(StaffRoles.FromRoleName).OfType<StaffRole>().Distinct().ToList();
    }

    private static Task<UserRow?> ReadUserAsync(DbConnection connection, DbTransaction? transaction, string userId, CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(
            "SELECT Id, FullName FROM Users WHERE Id = @UserId", new { UserId = userId }, transaction, cancellationToken: cancellationToken));

    private static Task<ProfileRow?> ReadProfileAsync(DbConnection connection, DbTransaction? transaction, string userId, CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<ProfileRow>(new CommandDefinition(
            "SELECT JobTitle, UsesCustomPermissions, Version FROM StaffProfiles WHERE UserId = @UserId",
            new { UserId = userId }, transaction, cancellationToken: cancellationToken));

    private static Task InsertProfileAsync(
        DbConnection connection, DbTransaction transaction, string userId, string? jobTitle, string changedBy, DateTime now, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO StaffProfiles (UserId, JobTitle, UsesCustomPermissions, Version, CreatedAtUtc, UpdatedAtUtc, UpdatedBy)
            VALUES (@UserId, @JobTitle, 0, 0, @Now, @Now, @UpdatedBy)
            """,
            new { UserId = userId, JobTitle = jobTitle, Now = now, UpdatedBy = changedBy }, transaction, cancellationToken: cancellationToken));

    private sealed class MemberRow
    {
        public string UserId { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string? PhoneNumber { get; init; }
        public bool EmailConfirmed { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public bool LockoutEnabled { get; init; }
        public DateTime? LockoutEndUtc { get; init; }
        public int HasPassword { get; init; }
        public int? StaffRank { get; init; }
        public int? RoleCount { get; init; }
        public string? ProfileUserId { get; init; }
        public string? JobTitle { get; init; }
        public bool? UsesCustomPermissions { get; init; }
        public DateTime? OnClockSinceUtc { get; init; }

        public StaffMemberDto ToDto(DateTime nowUtc)
        {
            var lockoutEnd = LockoutEndUtc is { } end ? DateTime.SpecifyKind(end, DateTimeKind.Utc) : (DateTime?)null;
            return new StaffMemberDto
            {
                UserId = UserId,
                FullName = FullName,
                Email = Email,
                PhoneNumber = PhoneNumber,
                Role = StaffRank is { } rank && Enum.IsDefined(typeof(StaffRole), rank) ? (StaffRole?)rank : null,
                JobTitle = JobTitle,
                UsesCustomPermissions = UsesCustomPermissions ?? false,
                HasPassword = HasPassword != 0,
                EmailConfirmed = EmailConfirmed,
                IsLockedOut = LockoutEnabled && lockoutEnd > nowUtc,
                LockoutEndUtc = lockoutEnd,
                CreatedAtUtc = DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc),
                OnClockSinceUtc = OnClockSinceUtc is { } since ? DateTime.SpecifyKind(since, DateTimeKind.Utc) : null,
                HasConflictingRoles = RoleCount > 1,
            };
        }
    }

    private sealed class RoleSetRow
    {
        public int Revision { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
        public string UpdatedBy { get; init; } = string.Empty;
    }

    private sealed class PermissionRow
    {
        public string Permission { get; init; } = string.Empty;
        public bool IsGranted { get; init; }
    }

    private sealed class UserRow
    {
        public string Id { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
    }

    private sealed class ProfileRow
    {
        public string? JobTitle { get; init; }
        public bool UsesCustomPermissions { get; init; }
        public int Version { get; init; }
    }
}
