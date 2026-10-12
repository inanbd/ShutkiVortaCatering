using Dapper;
using Microsoft.Extensions.Logging;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Infrastructure.Persistence;

namespace ShutkiVorta.Infrastructure.Identity;

/// <summary>
/// Reads a person's staff access in one round trip and evaluates it. Deliberately not cached between requests: the
/// query is tiny, and reading it fresh means a role or permission change applies on the very next request, on every
/// server. Within a request <see cref="ICurrentStaff"/> remembers the result. A singleton depending only on singletons.
/// </summary>
internal sealed class StaffAccessService(IDbConnectionFactory connections, ILogger<StaffAccessService> logger) : IStaffAccessService
{
    private const string Sql = """
        SELECT r.NormalizedName FROM UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId
         WHERE ur.UserId = @UserId AND r.NormalizedName IN ('ADMIN', 'MANAGER', 'EMPLOYEE');
        SELECT u.EmailConfirmed, u.SecurityStamp, sp.UsesCustomPermissions
          FROM Users u LEFT JOIN StaffProfiles sp ON sp.UserId = u.Id
         WHERE u.Id = @UserId;
        SELECT Permission FROM UserPermissions WHERE UserId = @UserId;
        SELECT Role, Permission, IsGranted FROM RolePermissions;
        """;

    public Task<StaffAccess> GetAsync(string userId, CancellationToken cancellationToken = default) =>
        ReadAsync(userId, checkStamp: false, null, cancellationToken);

    public Task<StaffAccess> GetForSignInAsync(string userId, string? securityStamp, CancellationToken cancellationToken = default) =>
        ReadAsync(userId, checkStamp: true, securityStamp, cancellationToken);

    private async Task<StaffAccess> ReadAsync(string userId, bool checkStamp, string? securityStamp, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var results = await connection.QueryMultipleAsync(new CommandDefinition(Sql, new { UserId = userId }, cancellationToken: cancellationToken));

        var roles = (await results.ReadAsync<string>())
            .Select(StaffRoles.FromRoleName)
            .OfType<StaffRole>()
            .Distinct()
            .ToList();
        var user = await results.ReadSingleOrDefaultAsync<UserRow>();
        var userGrants = (await results.ReadAsync<string>()).ToList();
        var overrides = (await results.ReadAsync<RolePermissionRow>())
            .Select(r => (Role: StaffRoles.FromRoleName(r.Role), r.Permission, r.IsGranted))
            .Where(r => r.Role is not null)
            .GroupBy(r => r.Role!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, bool>)g.ToDictionary(r => r.Permission, r => r.IsGranted, StringComparer.Ordinal));

        // Compared as Identity's own stamp validator does, which would otherwise notice only at its next check.
        if (user is null || (checkStamp && !string.Equals(user.SecurityStamp, securityStamp, StringComparison.Ordinal)))
        {
            return StaffAccess.None;
        }

        if (roles.Count > 1)
        {
            logger.LogWarning("User {UserId} holds several staff roles ({Roles}); the lowest one applies until an admin sets one role",
                userId, string.Join(", ", roles));
        }

        return StaffAccess.Evaluate(userId, roles, user.EmailConfirmed, user.UsesCustomPermissions ?? false, userGrants, overrides);
    }

    private sealed class UserRow
    {
        public bool EmailConfirmed { get; init; }
        public string? SecurityStamp { get; init; }
        public bool? UsesCustomPermissions { get; init; }
    }

    private sealed class RolePermissionRow
    {
        public string Role { get; init; } = string.Empty;
        public string Permission { get; init; } = string.Empty;
        public bool IsGranted { get; init; }
    }
}
