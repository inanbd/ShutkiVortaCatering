using Dapper;
using ShutkiVorta.Application.Features.Emails;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Infrastructure.Persistence;

namespace ShutkiVorta.Infrastructure.Email;

/// <summary>Reads the delivery status of staff emails from the EmailOutbox table (never their content).</summary>
internal sealed class StaffEmailLog(IDbConnectionFactory connections, ISqlDialect dialect) : IStaffEmailLog
{
    public async Task<StaffEmailStatusDto?> GetLatestAsync(string toAddress, IReadOnlyCollection<string> subjects, CancellationToken cancellationToken = default)
    {
        if (subjects.Count == 0)
        {
            return null;
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<Row>(new CommandDefinition(
            dialect.Page("""
                SELECT Id, Subject, Status, LastError, CreatedAtUtc, SentAtUtc FROM EmailOutbox
                WHERE ToAddresses = @To AND Subject IN @Subjects
                ORDER BY Id DESC
                """),
            new { To = toAddress, Subjects = subjects, Skip = 0, Take = 1 },
            cancellationToken: cancellationToken));

        return row is null
            ? null
            : new StaffEmailStatusDto
            {
                Id = row.Id,
                Subject = row.Subject,
                Status = (EmailStatus)row.Status,
                LastError = row.LastError,
                CreatedAtUtc = DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc),
                SentAtUtc = row.SentAtUtc is { } sent ? DateTime.SpecifyKind(sent, DateTimeKind.Utc) : null,
            };
    }

    private sealed class Row
    {
        public long Id { get; init; }
        public string Subject { get; init; } = string.Empty;
        public int Status { get; init; }
        public string? LastError { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime? SentAtUtc { get; init; }
    }
}
