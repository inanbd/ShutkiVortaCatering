using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace ShutkiVorta.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real web application against a throw-away database.
/// SQLite always runs; SQL Server runs when SHUTKIVORTA_TEST_SQLSERVER holds a connection string (without a database name).
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin12345!";
    public const string SqlServerEnvironmentVariable = "SHUTKIVORTA_TEST_SQLSERVER";

    private readonly string _sqlServerDatabase = $"ShutkiVortaTests_{Guid.NewGuid():N}";

    public AppFactory(string provider)
    {
        Provider = provider;
        TempDirectory = Path.Combine(Path.GetTempPath(), "shutkivorta-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDirectory);
    }

    public string Provider { get; }
    public string TempDirectory { get; }
    public string MailDirectory => Path.Combine(TempDirectory, "mail");

    public static string? SqlServerBaseConnection => Environment.GetEnvironmentVariable(SqlServerEnvironmentVariable);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = Provider,
            ["ConnectionStrings:Sqlite"] = $"Data Source={Path.Combine(TempDirectory, "test.db")}",
            ["ConnectionStrings:SqlServer"] = SqlServerConnectionString(),
            ["Email:DeliveryMethod"] = "PickupDirectory",
            ["Email:PickupDirectory"] = MailDirectory,
            ["Email:AdminRecipients:0"] = "kitchen@test.local",
            ["DataProtection:KeysPath"] = Path.Combine(TempDirectory, "keys"),
            ["UseHttpsRedirection"] = "false",
            ["Site:BaseUrl"] = "https://shutki.test",
            ["Seed:AdminEmail"] = AdminEmail,
            ["Seed:AdminPassword"] = AdminPassword,
            ["Ordering:MinimumLeadTimeHours"] = "24",
            ["Wholesale:AutoGenerate"] = "false", // tests drive the standing-order scheduler explicitly
        };

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
    }

    private string SqlServerConnectionString()
    {
        if (string.IsNullOrWhiteSpace(SqlServerBaseConnection))
        {
            return "Server=unused;Database=unused";
        }

        return new SqlConnectionStringBuilder(SqlServerBaseConnection) { InitialCatalog = _sqlServerDatabase }.ConnectionString;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (Provider == "SqlServer" && !string.IsNullOrWhiteSpace(SqlServerBaseConnection))
        {
            SqlConnection.ClearAllPools();
            var master = new SqlConnectionStringBuilder(SqlServerBaseConnection) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(master.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"IF DB_ID('{_sqlServerDatabase}') IS NOT NULL BEGIN ALTER DATABASE [{_sqlServerDatabase}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_sqlServerDatabase}]; END";
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            Directory.Delete(TempDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort clean-up (SQLite may still hold a WAL file briefly).
        }
    }
}
