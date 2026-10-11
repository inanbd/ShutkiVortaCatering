using System.Data.Common;
using Dapper;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Features.Staff;

namespace ShutkiVorta.Infrastructure.Persistence.Repositories;

internal sealed class StaffAuditLogRepository(IDbConnectionFactory connections, ISqlDialect dialect, IDateTimeProvider clock) : IStaffAuditLog
{
    private const string Columns = "Id, TargetUserId, TargetName, Action, Details, ChangedBy, ChangedAtUtc";

    public async Task WriteAsync(StaffAuditEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await InsertAsync(connection, null, entry, clock.UtcNow, cancellationToken);
    }

    public async Task<IReadOnlyList<StaffAuditEntryDto>> ListAsync(string? targetUserId, int take, CancellationToken cancellationToken = default)
    {
        var where = targetUserId is null ? string.Empty : "WHERE TargetUserId = @TargetUserId";
        var sql = dialect.Page($"SELECT {Columns} FROM StaffAuditLog {where} ORDER BY ChangedAtUtc DESC, Id DESC");

        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<AuditRow>(new CommandDefinition(
            sql, new { TargetUserId = targetUserId, Skip = 0, Take = Math.Clamp(take, 1, 500) }, cancellationToken: cancellationToken));
        return rows.Select(r => r.ToDto()).ToList();
    }

    public async Task<IReadOnlyList<StaffAuditEntryDto>> ListByActionsAsync(IReadOnlyCollection<string> actions, int take, CancellationToken cancellationToken = default)
    {
        if (actions.Count == 0)
        {
            return [];
        }

        var sql = dialect.Page($"SELECT {Columns} FROM StaffAuditLog WHERE Action IN @Actions ORDER BY ChangedAtUtc DESC, Id DESC");

        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<AuditRow>(new CommandDefinition(
            sql, new { Actions = actions.ToArray(), Skip = 0, Take = Math.Clamp(take, 1, 500) }, cancellationToken: cancellationToken));
        return rows.Select(r => r.ToDto()).ToList();
    }

    /// <summary>Writes an entry on the caller's connection, inside its transaction when given (so it commits with the change).</summary>
    internal static Task InsertAsync(DbConnection connection, DbTransaction? transaction, StaffAuditEntry entry, DateTime nowUtc, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO StaffAuditLog (TargetUserId, TargetName, Action, Details, ChangedBy, ChangedAtUtc)
            VALUES (@TargetUserId, @TargetName, @Action, @Details, @ChangedBy, @ChangedAtUtc)
            """,
            new
            {
                entry.TargetUserId,
                TargetName = Truncate(entry.TargetName, 120),
                Action = Truncate(entry.Action, 50),
                Details = Truncate(entry.Details, 2000),
                ChangedBy = Truncate(entry.ChangedBy, 256) ?? "System",
                ChangedAtUtc = nowUtc,
            },
            transaction,
            cancellationToken: cancellationToken));

    private static string? Truncate(string? value, int length) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= length ? value : value[..length];

    private sealed class AuditRow
    {
        public long Id { get; init; }
        public string? TargetUserId { get; init; }
        public string? TargetName { get; init; }
        public string Action { get; init; } = string.Empty;
        public string? Details { get; init; }
        public string ChangedBy { get; init; } = string.Empty;
        public DateTime ChangedAtUtc { get; init; }

        public StaffAuditEntryDto ToDto() => new()
        {
            Id = Id,
            TargetUserId = TargetUserId,
            TargetName = TargetName,
            Action = Action,
            Details = Details,
            ChangedBy = ChangedBy,
            ChangedAtUtc = DateTime.SpecifyKind(ChangedAtUtc, DateTimeKind.Utc),
        };
    }
}
