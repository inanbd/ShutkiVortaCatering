using System.Data.Common;

namespace ShutkiVorta.Infrastructure.Persistence;

public interface IDbConnectionFactory
{
    DatabaseProvider Provider { get; }

    /// <summary>Creates and opens a new connection. Callers own (and must dispose) the connection.</summary>
    Task<DbConnection> OpenAsync(CancellationToken cancellationToken = default);
}
