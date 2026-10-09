using MediatR;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.Application.Features.Emails;

public enum EmailStatus
{
    Pending = 0,
    Sending = 1,
    Sent = 2,
    Failed = 3,
    SavedToFolder = 4,
    Disabled = 5,
}

public static class EmailStatusNames
{
    public static string DisplayName(this EmailStatus status) => status switch
    {
        EmailStatus.Pending => "Waiting to send",
        EmailStatus.Sending => "Sending",
        EmailStatus.Sent => "Sent",
        EmailStatus.Failed => "Failed",
        EmailStatus.SavedToFolder => "Saved to folder (not sent)",
        EmailStatus.Disabled => "Not sent (email disabled)",
        _ => status.ToString(),
    };
}

public sealed class EmailLogEntryDto
{
    public long Id { get; init; }
    public string ToAddresses { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public EmailStatus Status { get; init; }
    public int Attempts { get; init; }
    public string? LastError { get; init; }
    public string? DeliveryMethod { get; init; }
    public string? DeliveryDetail { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? LastAttemptAtUtc { get; init; }
    public DateTime? SentAtUtc { get; init; }
    public DateTime? NextAttemptAtUtc { get; init; }
    public string? HtmlBody { get; init; }
    public string? TextBody { get; init; }

    /// <summary>Contains a private link; the content is never shown in the log.</summary>
    public bool IsSensitive { get; init; }
}

public sealed record EmailStatusCounts(int Pending, int Sent, int Failed, int SavedToFolder, int Disabled)
{
    public static readonly EmailStatusCounts Empty = new(0, 0, 0, 0, 0);
}

/// <summary>Durable outbox + delivery log for every email the site sends.</summary>
public interface IEmailLog
{
    Task<PagedResult<EmailLogEntryDto>> ListAsync(EmailStatus? status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<EmailLogEntryDto?> GetAsync(long id, CancellationToken cancellationToken = default);
    Task<bool> RetryAsync(long id, CancellationToken cancellationToken = default);
    Task<int> RetryAllFailedAsync(CancellationToken cancellationToken = default);
    Task<EmailStatusCounts> CountSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);
    Task RecordAsync(EmailMessage message, EmailStatus status, string? method, string? detail, string? error, CancellationToken cancellationToken = default);
}

/// <summary>Explains how email is currently configured and what looks wrong.</summary>
public interface IEmailDiagnostics
{
    EmailConfigurationReport GetReport();
}

public sealed record EmailConfigurationReport(
    bool Enabled,
    string ConfiguredMethod,
    string EffectiveMethod,
    string Summary,
    IReadOnlyList<string> Warnings)
{
    public bool SendsRealEmail => Enabled && EffectiveMethod == Common.Options.EmailDeliveryMethods.Smtp;
}

public sealed record GetEmailLogQuery(EmailStatus? Status, int Page = 1, int PageSize = 25) : IRequest<PagedResult<EmailLogEntryDto>>, IRequireAdmin;

public sealed record GetEmailLogEntryQuery(long Id) : IRequest<EmailLogEntryDto?>, IRequireAdmin;

public sealed record RetryEmailCommand(long Id) : IRequest<bool>, IRequireAdmin;

public sealed record RetryFailedEmailsCommand : IRequest<int>, IRequireAdmin;

public sealed record TestSmtpConnectionCommand : IRequest<SmtpConnectionTestResult>, IRequireAdmin;

internal sealed class EmailLogHandlers(IEmailLog log, IEmailTransport transport) :
    IRequestHandler<GetEmailLogQuery, PagedResult<EmailLogEntryDto>>,
    IRequestHandler<GetEmailLogEntryQuery, EmailLogEntryDto?>,
    IRequestHandler<RetryEmailCommand, bool>,
    IRequestHandler<RetryFailedEmailsCommand, int>,
    IRequestHandler<TestSmtpConnectionCommand, SmtpConnectionTestResult>
{
    public Task<PagedResult<EmailLogEntryDto>> Handle(GetEmailLogQuery request, CancellationToken cancellationToken)
    {
        var (page, size) = Paging.Normalize(request.Page, request.PageSize);
        return log.ListAsync(request.Status, page, size, cancellationToken);
    }

    public Task<EmailLogEntryDto?> Handle(GetEmailLogEntryQuery request, CancellationToken cancellationToken) =>
        log.GetAsync(request.Id, cancellationToken);

    public Task<bool> Handle(RetryEmailCommand request, CancellationToken cancellationToken) =>
        log.RetryAsync(request.Id, cancellationToken);

    public Task<int> Handle(RetryFailedEmailsCommand request, CancellationToken cancellationToken) =>
        log.RetryAllFailedAsync(cancellationToken);

    public Task<SmtpConnectionTestResult> Handle(TestSmtpConnectionCommand request, CancellationToken cancellationToken) =>
        transport.TestConnectionAsync(cancellationToken);
}
