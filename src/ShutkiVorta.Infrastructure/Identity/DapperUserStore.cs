using Dapper;
using Microsoft.AspNetCore.Identity;
using ShutkiVorta.Infrastructure.Persistence;

namespace ShutkiVorta.Infrastructure.Identity;

/// <summary>ASP.NET Core Identity user store implemented with Dapper, working on SQLite and SQL Server.</summary>
internal sealed class DapperUserStore(IDbConnectionFactory connections, IdentityErrorDescriber errors) :
    IUserPasswordStore<ApplicationUser>,
    IUserEmailStore<ApplicationUser>,
    IUserRoleStore<ApplicationUser>,
    IUserSecurityStampStore<ApplicationUser>,
    IUserLockoutStore<ApplicationUser>,
    IUserPhoneNumberStore<ApplicationUser>
{
    internal const string Columns = """
        Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp,
        ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, LockoutEnabled, LockoutEndUtc, AccessFailedCount,
        FullName, AddressLine1, AddressLine2, City, State, PostalCode, CreatedAtUtc
        """;

    public void Dispose()
    {
    }

    // ---- IUserStore -------------------------------------------------------------------------

    public async Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string sql = """
            INSERT INTO Users (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp,
                ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, LockoutEnabled, LockoutEndUtc, AccessFailedCount,
                FullName, AddressLine1, AddressLine2, City, State, PostalCode, CreatedAtUtc)
            VALUES (@Id, @UserName, @NormalizedUserName, @Email, @NormalizedEmail, @EmailConfirmed, @PasswordHash, @SecurityStamp,
                @ConcurrencyStamp, @PhoneNumber, @PhoneNumberConfirmed, @LockoutEnabled, @LockoutEndUtc, @AccessFailedCount,
                @FullName, @AddressLine1, @AddressLine2, @City, @State, @PostalCode, @CreatedAtUtc)
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, user, cancellationToken: cancellationToken));
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string sql = """
            UPDATE Users SET
                UserName = @UserName, NormalizedUserName = @NormalizedUserName, Email = @Email, NormalizedEmail = @NormalizedEmail,
                EmailConfirmed = @EmailConfirmed, PasswordHash = @PasswordHash, SecurityStamp = @SecurityStamp,
                ConcurrencyStamp = @NewConcurrencyStamp, PhoneNumber = @PhoneNumber, PhoneNumberConfirmed = @PhoneNumberConfirmed,
                LockoutEnabled = @LockoutEnabled, LockoutEndUtc = @LockoutEndUtc, AccessFailedCount = @AccessFailedCount,
                FullName = @FullName, AddressLine1 = @AddressLine1, AddressLine2 = @AddressLine2, City = @City,
                State = @State, PostalCode = @PostalCode
            WHERE Id = @Id AND (ConcurrencyStamp = @ConcurrencyStamp OR (ConcurrencyStamp IS NULL AND @ConcurrencyStamp IS NULL))
            """;

        var newStamp = Guid.NewGuid().ToString();
        var parameters = new DynamicParameters(user);
        parameters.Add("NewConcurrencyStamp", newStamp);

        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        if (rows == 0)
        {
            return IdentityResult.Failed(errors.ConcurrencyFailure());
        }

        user.ConcurrencyStamp = newStamp;
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM Users WHERE Id = @Id", new { user.Id }, cancellationToken: cancellationToken));
        return IdentityResult.Success;
    }

    public async Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ApplicationUser>(new CommandDefinition(
            $"SELECT {Columns} FROM Users WHERE Id = @Id", new { Id = userId }, cancellationToken: cancellationToken));
    }

    public async Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ApplicationUser>(new CommandDefinition(
            $"SELECT {Columns} FROM Users WHERE NormalizedUserName = @Name", new { Name = normalizedUserName }, cancellationToken: cancellationToken));
    }

    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id);

    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult<string?>(user.UserName);

    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName ?? string.Empty;
        return Task.CompletedTask;
    }

    // ---- Password ---------------------------------------------------------------------------

    public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));

    // ---- Email ------------------------------------------------------------------------------

    public Task SetEmailAsync(ApplicationUser user, string? email, CancellationToken cancellationToken)
    {
        user.Email = email ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult<string?>(user.Email);

    public Task<bool> GetEmailConfirmedAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.EmailConfirmed);

    public Task SetEmailConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken cancellationToken)
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public async Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ApplicationUser>(new CommandDefinition(
            $"SELECT {Columns} FROM Users WHERE NormalizedEmail = @Email", new { Email = normalizedEmail }, cancellationToken: cancellationToken));
    }

    public Task<string?> GetNormalizedEmailAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(ApplicationUser user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedEmail ?? string.Empty;
        return Task.CompletedTask;
    }

    // ---- Roles ------------------------------------------------------------------------------

    public async Task AddToRoleAsync(ApplicationUser user, string normalizedRoleName, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var roleId = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT Id FROM Roles WHERE NormalizedName = @Name", new { Name = normalizedRoleName }, cancellationToken: cancellationToken))
            ?? throw new InvalidOperationException($"Role '{normalizedRoleName}' does not exist.");

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO UserRoles (UserId, RoleId) SELECT @UserId, @RoleId WHERE NOT EXISTS (SELECT 1 FROM UserRoles WHERE UserId = @UserId AND RoleId = @RoleId)",
            new { UserId = user.Id, RoleId = roleId },
            cancellationToken: cancellationToken));
    }

    public async Task RemoveFromRoleAsync(ApplicationUser user, string normalizedRoleName, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM UserRoles WHERE UserId = @UserId AND RoleId IN (SELECT Id FROM Roles WHERE NormalizedName = @Name)",
            new { UserId = user.Id, Name = normalizedRoleName },
            cancellationToken: cancellationToken));
    }

    public async Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var roles = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT r.Name FROM Roles r INNER JOIN UserRoles ur ON ur.RoleId = r.Id WHERE ur.UserId = @UserId",
            new { UserId = user.Id },
            cancellationToken: cancellationToken));
        return roles.ToList();
    }

    public async Task<bool> IsInRoleAsync(ApplicationUser user, string normalizedRoleName, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId WHERE ur.UserId = @UserId AND r.NormalizedName = @Name",
            new { UserId = user.Id, Name = normalizedRoleName },
            cancellationToken: cancellationToken));
        return count > 0;
    }

    public async Task<IList<ApplicationUser>> GetUsersInRoleAsync(string normalizedRoleName, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var users = await connection.QueryAsync<ApplicationUser>(new CommandDefinition(
            $"SELECT {Columns} FROM Users WHERE Id IN (SELECT ur.UserId FROM UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId WHERE r.NormalizedName = @Name)",
            new { Name = normalizedRoleName },
            cancellationToken: cancellationToken));
        return users.ToList();
    }

    // ---- Security stamp ---------------------------------------------------------------------

    public Task SetSecurityStampAsync(ApplicationUser user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.SecurityStamp);

    // ---- Lockout ----------------------------------------------------------------------------

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEndUtc is { } end
            ? new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Utc))
            : (DateTimeOffset?)null);

    public Task SetLockoutEndDateAsync(ApplicationUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        user.LockoutEndUtc = lockoutEnd?.UtcDateTime;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount++;
        return Task.FromResult(user.AccessFailedCount);
    }

    public Task ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.AccessFailedCount);

    public Task<bool> GetLockoutEnabledAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.LockoutEnabled);

    public Task SetLockoutEnabledAsync(ApplicationUser user, bool enabled, CancellationToken cancellationToken)
    {
        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }

    // ---- Phone ------------------------------------------------------------------------------

    public Task SetPhoneNumberAsync(ApplicationUser user, string? phoneNumber, CancellationToken cancellationToken)
    {
        user.PhoneNumber = phoneNumber;
        return Task.CompletedTask;
    }

    public Task<string?> GetPhoneNumberAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.PhoneNumber);

    public Task<bool> GetPhoneNumberConfirmedAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PhoneNumberConfirmed);

    public Task SetPhoneNumberConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken cancellationToken)
    {
        user.PhoneNumberConfirmed = confirmed;
        return Task.CompletedTask;
    }
}
