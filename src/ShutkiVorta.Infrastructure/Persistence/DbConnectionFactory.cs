using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using ShutkiVorta.Application.Features.Settings;

namespace ShutkiVorta.Infrastructure.Persistence;

internal sealed class DbConnectionFactory : IDbConnectionFactory, IDatabaseInfo
{
    private readonly string _connectionString;

    public DbConnectionFactory(DatabaseOptions options)
    {
        Provider = options.Provider;
        _connectionString = options.ConnectionString;
    }

    public DatabaseProvider Provider { get; }

    public string ProviderName => Provider == DatabaseProvider.Sqlite ? "SQLite" : "Microsoft SQL Server";

    public string DataSource => Provider switch
    {
        DatabaseProvider.Sqlite => new SqliteConnectionStringBuilder(_connectionString).DataSource,
        _ => $"{new SqlConnectionStringBuilder(_connectionString).DataSource} / {new SqlConnectionStringBuilder(_connectionString).InitialCatalog}",
    };

    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        DbConnection connection = Provider switch
        {
            DatabaseProvider.Sqlite => new SqliteConnection(_connectionString),
            DatabaseProvider.SqlServer => new SqlConnection(_connectionString),
            _ => throw new NotSupportedException($"Database provider '{Provider}' is not supported."),
        };

        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
