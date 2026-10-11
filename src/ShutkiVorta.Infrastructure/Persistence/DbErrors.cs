using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace ShutkiVorta.Infrastructure.Persistence;

/// <summary>Recognises database errors that callers handle as expected situations, on both providers.</summary>
public static class DbErrors
{
    /// <summary>
    /// A unique index or primary key rejected a row (e.g. a second open shift, or a row another request inserted first).
    /// SQLite: SQLITE_CONSTRAINT_UNIQUE (2067) or SQLITE_CONSTRAINT_PRIMARYKEY (1555), not other constraint failures.
    /// SQL Server: 2601 (unique index) or 2627 (unique or primary key constraint).
    /// </summary>
    public static bool IsUniqueViolation(Exception exception) => exception switch
    {
        SqliteException sqlite => sqlite.SqliteExtendedErrorCode is 2067 or 1555,
        SqlException sql => sql.Number is 2601 or 2627,
        _ => false,
    };
}
