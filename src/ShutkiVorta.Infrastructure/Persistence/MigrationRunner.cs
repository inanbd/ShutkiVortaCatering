using System.Data.Common;
using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Logging;

namespace ShutkiVorta.Infrastructure.Persistence;

/// <summary>
/// Minimal, dependency-free schema migrator. Scripts are embedded resources under
/// Persistence/Migrations/{Sqlite|SqlServer}/NNNN_Name.sql and are applied once, in order, each inside a transaction.
/// </summary>
internal sealed partial class MigrationRunner(IDbConnectionFactory connections, ILogger<MigrationRunner> logger)
{
    private const string HistoryTable = "__SchemaMigrations";

    public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await EnsureHistoryTableAsync(connection);

        var applied = (await connection.QueryAsync<string>($"SELECT Id FROM {HistoryTable}")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = GetScripts().Where(s => !applied.Contains(s.Id)).ToList();

        foreach (var script in pending)
        {
            logger.LogInformation("Applying database migration {Migration} ({Provider})", script.Id, connections.Provider);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var batch in SplitBatches(script.Sql))
            {
                await connection.ExecuteAsync(new CommandDefinition(batch, transaction: transaction, cancellationToken: cancellationToken));
            }

            await connection.ExecuteAsync(
                new CommandDefinition(
                    $"INSERT INTO {HistoryTable} (Id, AppliedAtUtc) VALUES (@Id, @AppliedAtUtc)",
                    new { script.Id, AppliedAtUtc = DateTime.UtcNow },
                    transaction,
                    cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }

        return pending.Select(p => p.Id).ToList();
    }

    private async Task EnsureHistoryTableAsync(DbConnection connection)
    {
        var sql = connections.Provider == DatabaseProvider.Sqlite
            ? $"CREATE TABLE IF NOT EXISTS {HistoryTable} (Id TEXT NOT NULL PRIMARY KEY, AppliedAtUtc TEXT NOT NULL);"
            : $"IF OBJECT_ID(N'dbo.{HistoryTable}', N'U') IS NULL CREATE TABLE dbo.{HistoryTable} (Id NVARCHAR(150) NOT NULL PRIMARY KEY, AppliedAtUtc DATETIME2 NOT NULL);";
        await connection.ExecuteAsync(sql);
    }

    private IEnumerable<(string Id, string Sql)> GetScripts()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = $"{typeof(MigrationRunner).Namespace}.Migrations.{connections.Provider}.";

        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(n =>
            {
                using var stream = assembly.GetManifestResourceStream(n)!;
                using var reader = new StreamReader(stream);
                var id = n[prefix.Length..^".sql".Length];
                return (Id: id, Sql: reader.ReadToEnd());
            })
            .OrderBy(s => s.Id, StringComparer.Ordinal);
    }

    /// <summary>Splits SQL Server scripts on "GO" separator lines; SQLite scripts run as a single batch.</summary>
    private IEnumerable<string> SplitBatches(string sql) =>
        connections.Provider == DatabaseProvider.SqlServer
            ? GoSeparator().Split(sql).Where(b => !string.IsNullOrWhiteSpace(b))
            : [sql];

    [GeneratedRegex(@"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
