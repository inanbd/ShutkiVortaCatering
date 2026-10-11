using Dapper;
using ShutkiVorta.Application.Features.TimeClock;

namespace ShutkiVorta.Infrastructure.Persistence.Repositories;

/// <summary>Reads open shifts (at most one per person, guaranteed by IX_TimeClockShifts_OpenShift).</summary>
internal sealed class TimeClockStatusReader(IDbConnectionFactory connections) : ITimeClockStatusReader
{
    // Keeps each query well below SQL Server's limit of 2100 parameters.
    private const int BatchSize = 500;

    public async Task<IReadOnlyDictionary<string, DateTime>> GetOpenShiftStartsAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        if (userIds.Count == 0)
        {
            return result;
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        foreach (var batch in userIds.Distinct(StringComparer.Ordinal).Chunk(BatchSize))
        {
            var rows = await connection.QueryAsync<OpenShiftRow>(new CommandDefinition(
                "SELECT UserId, ClockInUtc FROM TimeClockShifts WHERE ClockOutUtc IS NULL AND UserId IN @UserIds",
                new { UserIds = batch }, cancellationToken: cancellationToken));
            foreach (var row in rows)
            {
                result[row.UserId] = DateTime.SpecifyKind(row.ClockInUtc, DateTimeKind.Utc);
            }
        }

        return result;
    }

    public async Task<DateTime?> GetOpenShiftStartAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var open = await connection.QuerySingleOrDefaultAsync<OpenShiftRow>(new CommandDefinition(
            "SELECT UserId, ClockInUtc FROM TimeClockShifts WHERE UserId = @UserId AND ClockOutUtc IS NULL",
            new { UserId = userId }, cancellationToken: cancellationToken));
        return open is null ? null : DateTime.SpecifyKind(open.ClockInUtc, DateTimeKind.Utc);
    }

    private sealed class OpenShiftRow
    {
        public string UserId { get; init; } = string.Empty;
        public DateTime ClockInUtc { get; init; }
    }
}
