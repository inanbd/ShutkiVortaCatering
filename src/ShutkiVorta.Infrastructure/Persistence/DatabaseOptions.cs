namespace ShutkiVorta.Infrastructure.Persistence;

public enum DatabaseProvider
{
    Sqlite,
    SqlServer,
}

/// <summary>Bound from the "Database" section. The connection string is read from ConnectionStrings:{Provider}.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>Apply pending schema migrations at startup.</summary>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>SQL Server only: create the database if it does not exist.</summary>
    public bool AutoCreateDatabase { get; set; } = true;

    /// <summary>Resolved connection string (set during registration).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    public int CommandTimeoutSeconds { get; set; } = 30;
}
