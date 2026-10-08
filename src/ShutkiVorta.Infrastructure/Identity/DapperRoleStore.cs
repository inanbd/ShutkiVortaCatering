using Dapper;
using Microsoft.AspNetCore.Identity;
using ShutkiVorta.Infrastructure.Persistence;

namespace ShutkiVorta.Infrastructure.Identity;

internal sealed class DapperRoleStore(IDbConnectionFactory connections) : IRoleStore<ApplicationRole>
{
    public void Dispose()
    {
    }

    public async Task<IdentityResult> CreateAsync(ApplicationRole role, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO Roles (Id, Name, NormalizedName, ConcurrencyStamp) VALUES (@Id, @Name, @NormalizedName, @ConcurrencyStamp)",
            role,
            cancellationToken: cancellationToken));
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(ApplicationRole role, CancellationToken cancellationToken)
    {
        role.ConcurrencyStamp = Guid.NewGuid().ToString();
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE Roles SET Name = @Name, NormalizedName = @NormalizedName, ConcurrencyStamp = @ConcurrencyStamp WHERE Id = @Id",
            role,
            cancellationToken: cancellationToken));
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(ApplicationRole role, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM Roles WHERE Id = @Id", new { role.Id }, cancellationToken: cancellationToken));
        return IdentityResult.Success;
    }

    public async Task<ApplicationRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ApplicationRole>(new CommandDefinition(
            "SELECT Id, Name, NormalizedName, ConcurrencyStamp FROM Roles WHERE Id = @Id", new { Id = roleId }, cancellationToken: cancellationToken));
    }

    public async Task<ApplicationRole?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ApplicationRole>(new CommandDefinition(
            "SELECT Id, Name, NormalizedName, ConcurrencyStamp FROM Roles WHERE NormalizedName = @Name",
            new { Name = normalizedRoleName },
            cancellationToken: cancellationToken));
    }

    public Task<string> GetRoleIdAsync(ApplicationRole role, CancellationToken cancellationToken) => Task.FromResult(role.Id);

    public Task<string?> GetRoleNameAsync(ApplicationRole role, CancellationToken cancellationToken) => Task.FromResult<string?>(role.Name);

    public Task SetRoleNameAsync(ApplicationRole role, string? roleName, CancellationToken cancellationToken)
    {
        role.Name = roleName ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedRoleNameAsync(ApplicationRole role, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(role.NormalizedName);

    public Task SetNormalizedRoleNameAsync(ApplicationRole role, string? normalizedName, CancellationToken cancellationToken)
    {
        role.NormalizedName = normalizedName ?? string.Empty;
        return Task.CompletedTask;
    }
}
