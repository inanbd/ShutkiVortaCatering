using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShutkiVorta.Infrastructure.Persistence.Seed;

namespace ShutkiVorta.Infrastructure.Persistence;

/// <summary>Creates the database if needed, applies migrations and seeds roles, the admin user and the starter menu.</summary>
public sealed class DatabaseInitializer
{
    private readonly DatabaseOptions _options;
    private readonly IServiceProvider _services;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(DatabaseOptions options, IServiceProvider services, ILogger<DatabaseInitializer> logger)
    {
        _options = options;
        _services = services;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing {Provider} database", _options.Provider);

        if (_options.Provider == DatabaseProvider.Sqlite)
        {
            await PrepareSqliteAsync(cancellationToken);
        }
        else if (_options.AutoCreateDatabase)
        {
            await EnsureSqlServerDatabaseAsync(cancellationToken);
        }

        if (_options.AutoMigrate)
        {
            var applied = await _services.GetRequiredService<MigrationRunner>().MigrateAsync(cancellationToken);
            if (applied.Count > 0)
            {
                _logger.LogInformation("Applied {Count} migration(s): {Migrations}", applied.Count, string.Join(", ", applied));
            }
        }

        await _services.GetRequiredService<DatabaseSeeder>().SeedAsync(cancellationToken);
    }

    private async Task PrepareSqliteAsync(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder(_options.ConnectionString);
        if (builder.Mode != SqliteOpenMode.Memory && builder.DataSource != ":memory:")
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(builder.DataSource));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        // Write-ahead logging lets readers and the writer work concurrently.
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync("PRAGMA journal_mode=WAL;");
    }

    private async Task EnsureSqlServerDatabaseAsync(CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(_options.ConnectionString);
        var database = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(database))
        {
            return;
        }

        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            "IF DB_ID(@Name) IS NULL BEGIN DECLARE @sql NVARCHAR(MAX) = N'CREATE DATABASE ' + QUOTENAME(@Name); EXEC (@sql); END",
            new { Name = database });
    }
}
