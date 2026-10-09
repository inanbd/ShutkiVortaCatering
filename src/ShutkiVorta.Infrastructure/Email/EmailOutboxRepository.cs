using Dapper;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Features.Emails;
using ShutkiVorta.Infrastructure.Persistence;

namespace ShutkiVorta.Infrastructure.Email;

/// <summary>Dapper access to the EmailOutbox table: the durable queue the dispatcher drains and the admin email log.</summary>
internal sealed class EmailOutboxRepository(IDbConnectionFactory connections, ISqlDialect dialect, TimeProvider time) : IEmailLog
{
    private const string SummaryColumns = """
        Id, ToAddresses, Subject, Status, Attempts, LastError, DeliveryMethod, DeliveryDetail,
        CreatedAtUtc, LastAttemptAtUtc, SentAtUtc, NextAttemptAtUtc, IsSensitive
        """;

    /// <summary>Replaces the content of a sensitive email once it no longer needs to be sent (see EmailMessage.Sensitive).</summary>
    private const string RedactSensitive = """
        HtmlBody = CASE WHEN IsSensitive = 1 THEN @Redacted ELSE HtmlBody END,
        TextBody = CASE WHEN IsSensitive = 1 THEN @Redacted ELSE TextBody END
        """;

    private const string RedactedBody = "[Removed after delivery: this email contained a private sign-in link.]";

    /// <summary>An email claimed by "Sending" for longer than this is assumed lost (e.g. the app restarted) and retried.</summary>
    private static readonly TimeSpan StaleSendingAfter = TimeSpan.FromMinutes(10);

    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    public async Task EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var now = UtcNow;
        await InsertAsync(message, EmailStatus.Pending, null, null, null, now, nextAttemptAtUtc: now, sentAtUtc: null, attempts: 0, cancellationToken);
    }

    public Task RecordAsync(EmailMessage message, EmailStatus status, string? method, string? detail, string? error, CancellationToken cancellationToken = default)
    {
        var now = UtcNow;
        var sentAt = status is EmailStatus.Sent or EmailStatus.SavedToFolder ? now : (DateTime?)null;
        return InsertAsync(message, status, method, detail, error, now, nextAttemptAtUtc: null, sentAt, attempts: 1, cancellationToken);
    }

    /// <summary>Atomically claims due emails (one dispatcher wins each row, even with several app instances).</summary>
    public async Task<IReadOnlyList<OutboxEmail>> ClaimDueAsync(int take, CancellationToken cancellationToken = default)
    {
        var now = UtcNow;
        var stale = now - StaleSendingAfter;
        await using var connection = await connections.OpenAsync(cancellationToken);

        var candidates = await connection.QueryAsync<long>(new CommandDefinition(
            dialect.Page("""
                SELECT Id FROM EmailOutbox
                WHERE (Status = @Pending AND NextAttemptAtUtc <= @Now) OR (Status = @Sending AND LastAttemptAtUtc < @Stale)
                ORDER BY Id
                """),
            new { Pending = (int)EmailStatus.Pending, Sending = (int)EmailStatus.Sending, Now = now, Stale = stale, Skip = 0, Take = take },
            cancellationToken: cancellationToken));

        var claimed = new List<OutboxEmail>();
        foreach (var id in candidates)
        {
            var rows = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE EmailOutbox SET Status = @Sending, Attempts = Attempts + 1, LastAttemptAtUtc = @Now
                WHERE Id = @Id AND ((Status = @Pending AND NextAttemptAtUtc <= @Now) OR (Status = @Sending AND LastAttemptAtUtc < @Stale))
                """,
                new { Id = id, Pending = (int)EmailStatus.Pending, Sending = (int)EmailStatus.Sending, Now = now, Stale = stale },
                cancellationToken: cancellationToken));

            if (rows == 1)
            {
                var email = await connection.QuerySingleAsync<OutboxEmail>(new CommandDefinition(
                    "SELECT Id, ToAddresses, ReplyTo, Subject, HtmlBody, TextBody, Attempts FROM EmailOutbox WHERE Id = @Id",
                    new { Id = id },
                    cancellationToken: cancellationToken));
                claimed.Add(email);
            }
        }

        return claimed;
    }

    public async Task MarkDeliveredAsync(long id, EmailDeliveryResult result, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            $"""
            UPDATE EmailOutbox SET Status = @Status, DeliveryMethod = @Method, DeliveryDetail = @Detail, LastError = NULL,
                SentAtUtc = @Now, NextAttemptAtUtc = NULL, {RedactSensitive}
            WHERE Id = @Id
            """,
            new
            {
                Id = id,
                Status = (int)(result.ActuallySent ? EmailStatus.Sent : EmailStatus.SavedToFolder),
                result.Method,
                Detail = Truncate(result.Detail, 1000),
                Now = UtcNow,
                Redacted = RedactedBody,
            },
            cancellationToken: cancellationToken));
    }

    public async Task MarkFailedAsync(long id, string method, string error, DateTime? retryAtUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            $"""
            UPDATE EmailOutbox SET Status = @Status, DeliveryMethod = @Method, LastError = @Error, NextAttemptAtUtc = @RetryAt{(retryAtUtc is null ? ", " + RedactSensitive : string.Empty)}
            WHERE Id = @Id
            """,
            new
            {
                Id = id,
                Status = (int)(retryAtUtc is null ? EmailStatus.Failed : EmailStatus.Pending),
                Method = method,
                Error = Truncate(error, 4000),
                RetryAt = retryAtUtc,
                Redacted = RedactedBody,
            },
            cancellationToken: cancellationToken));
    }

    public async Task MarkDisabledAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE EmailOutbox SET Status = @Status, LastError = @Error, NextAttemptAtUtc = NULL, {RedactSensitive} WHERE Id = @Id",
            new { Id = id, Status = (int)EmailStatus.Disabled, Error = "Email:Enabled is false — not sent.", Redacted = RedactedBody },
            cancellationToken: cancellationToken));
    }

    public async Task<PagedResult<EmailLogEntryDto>> ListAsync(EmailStatus? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var where = status is null ? string.Empty : "WHERE Status = @Status";
        await using var connection = await connections.OpenAsync(cancellationToken);
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) FROM EmailOutbox {where}", new { Status = (int?)status }, cancellationToken: cancellationToken));
        if (total == 0)
        {
            return PagedResult<EmailLogEntryDto>.Empty(page, pageSize);
        }

        var items = await connection.QueryAsync<EmailLogEntryDto>(new CommandDefinition(
            dialect.Page($"SELECT {SummaryColumns} FROM EmailOutbox {where} ORDER BY Id DESC"),
            new { Status = (int?)status, Skip = (page - 1) * pageSize, Take = pageSize },
            cancellationToken: cancellationToken));
        return new PagedResult<EmailLogEntryDto>(items.AsList(), total, page, pageSize);
    }

    public async Task<EmailLogEntryDto?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<EmailLogEntryDto>(new CommandDefinition(
            $"""
            SELECT {SummaryColumns},
                CASE WHEN IsSensitive = 1 THEN NULL ELSE HtmlBody END AS HtmlBody,
                CASE WHEN IsSensitive = 1 THEN NULL ELSE TextBody END AS TextBody
            FROM EmailOutbox WHERE Id = @Id
            """,
            new { Id = id },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> RetryAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE EmailOutbox SET Status = @Pending, NextAttemptAtUtc = @Now, Attempts = 0 WHERE Id = @Id AND IsSensitive = 0 AND Status IN (@Failed, @Disabled, @SavedToFolder)",
            new
            {
                Id = id,
                Pending = (int)EmailStatus.Pending,
                Failed = (int)EmailStatus.Failed,
                Disabled = (int)EmailStatus.Disabled,
                SavedToFolder = (int)EmailStatus.SavedToFolder,
                Now = UtcNow,
            },
            cancellationToken: cancellationToken));
        return rows == 1;
    }

    public async Task<int> RetryAllFailedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE EmailOutbox SET Status = @Pending, NextAttemptAtUtc = @Now, Attempts = 0 WHERE Status = @Failed AND IsSensitive = 0",
            new { Pending = (int)EmailStatus.Pending, Failed = (int)EmailStatus.Failed, Now = UtcNow },
            cancellationToken: cancellationToken));
    }

    public async Task<EmailStatusCounts> CountSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = (await connection.QueryAsync<StatusCount>(new CommandDefinition(
            "SELECT Status, COUNT(*) AS Total FROM EmailOutbox WHERE CreatedAtUtc >= @Since GROUP BY Status",
            new { Since = sinceUtc },
            cancellationToken: cancellationToken))).ToDictionary(r => (EmailStatus)r.Status, r => r.Total);

        int Count(params EmailStatus[] statuses) => statuses.Sum(s => rows.TryGetValue(s, out var c) ? c : 0);
        return new EmailStatusCounts(
            Count(EmailStatus.Pending, EmailStatus.Sending),
            Count(EmailStatus.Sent),
            Count(EmailStatus.Failed),
            Count(EmailStatus.SavedToFolder),
            Count(EmailStatus.Disabled));
    }

    private async Task InsertAsync(
        EmailMessage message, EmailStatus status, string? method, string? detail, string? error, DateTime now,
        DateTime? nextAttemptAtUtc, DateTime? sentAtUtc, int attempts, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO EmailOutbox (ToAddresses, ReplyTo, Subject, HtmlBody, TextBody, Status, Attempts, LastError, DeliveryMethod,
                DeliveryDetail, CreatedAtUtc, NextAttemptAtUtc, LastAttemptAtUtc, SentAtUtc, IsSensitive)
            VALUES (@ToAddresses, @ReplyTo, @Subject, @HtmlBody, @TextBody, @Status, @Attempts, @LastError, @DeliveryMethod,
                @DeliveryDetail, @CreatedAtUtc, @NextAttemptAtUtc, @LastAttemptAtUtc, @SentAtUtc, @IsSensitive)
            """,
            new
            {
                ToAddresses = Truncate(string.Join(", ", message.To), 2000),
                ReplyTo = Truncate(message.ReplyTo, 256),
                Subject = Truncate(message.Subject, 500),
                HtmlBody = message.Sensitive && status != EmailStatus.Pending ? RedactedBody : message.HtmlBody,
                TextBody = message.Sensitive && status != EmailStatus.Pending ? RedactedBody : message.TextBody,
                IsSensitive = message.Sensitive,
                Status = (int)status,
                Attempts = attempts,
                LastError = Truncate(error, 4000),
                DeliveryMethod = method,
                DeliveryDetail = Truncate(detail, 1000),
                CreatedAtUtc = now,
                NextAttemptAtUtc = nextAttemptAtUtc,
                LastAttemptAtUtc = attempts > 0 ? now : (DateTime?)null,
                SentAtUtc = sentAtUtc,
            },
            cancellationToken: cancellationToken));
    }

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];

    private sealed class StatusCount
    {
        public int Status { get; init; }
        public int Total { get; init; }
    }
}

/// <summary>An email claimed from the outbox for delivery.</summary>
internal sealed class OutboxEmail
{
    public long Id { get; init; }
    public string ToAddresses { get; init; } = string.Empty;
    public string? ReplyTo { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string HtmlBody { get; init; } = string.Empty;
    public string TextBody { get; init; } = string.Empty;
    public int Attempts { get; init; }

    public EmailMessage ToMessage() => new(
        ToAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        Subject,
        HtmlBody,
        TextBody,
        ReplyTo);
}
