using Dapper;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Features.Staff;

namespace ShutkiVorta.Infrastructure.Persistence.Repositories;

/// <summary>
/// Claims a person's StaffProfiles.Version before a change the store takes no version for. Takes the same lock as
/// StaffRepository (the ADMIN row of Roles), so a claim waits for a role change or permission save in progress and two
/// claims for someone without a profile cannot both create one.
/// </summary>
internal sealed class StaffProfileVersions(IDbConnectionFactory connections, IDateTimeProvider clock) : IStaffProfileVersions
{
    public async Task<bool> ClaimAsync(string userId, int expectedVersion, string changedBy, CancellationToken cancellationToken = default)
    {
        var parameters = new { UserId = userId, Expected = expectedVersion, Now = clock.UtcNow, UpdatedBy = changedBy };

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE Roles SET ConcurrencyStamp = ConcurrencyStamp WHERE NormalizedName = 'ADMIN'", transaction: transaction, cancellationToken: cancellationToken));

        var claimed = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE StaffProfiles SET Version = Version + 1, UpdatedAtUtc = @Now, UpdatedBy = @UpdatedBy WHERE UserId = @UserId AND Version = @Expected",
            parameters, transaction, cancellationToken: cancellationToken));

        if (claimed == 0 && expectedVersion == 0)
        {
            // Staff who joined before profiles existed (e.g. the first admin) have none: their page showed version 0.
            claimed = await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO StaffProfiles (UserId, JobTitle, UsesCustomPermissions, Version, CreatedAtUtc, UpdatedAtUtc, UpdatedBy)
                SELECT u.Id, NULL, 0, 1, @Now, @Now, @UpdatedBy FROM Users u
                 WHERE u.Id = @UserId AND NOT EXISTS (SELECT 1 FROM StaffProfiles p WHERE p.UserId = u.Id)
                """,
                parameters, transaction, cancellationToken: cancellationToken));
        }

        if (claimed == 0)
        {
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
