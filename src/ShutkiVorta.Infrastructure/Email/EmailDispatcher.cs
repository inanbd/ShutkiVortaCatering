using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Emails;

namespace ShutkiVorta.Infrastructure.Email;

/// <summary>
/// Background service that delivers emails from the durable outbox. Temporary failures (network, timeouts, 4xx replies)
/// are retried with back-off (1 min, 5 min, 30 min, 2 h); permanent ones (wrong password, certificate, 5xx) fail at once
/// with a hint. Everything is visible in Admin → Email log, where failed emails can be re-sent after fixing the settings.
/// </summary>
internal sealed class EmailDispatcher(
    EmailDispatchSignal signal,
    IServiceScopeFactory scopes,
    IEmailDiagnostics diagnostics,
    IOptionsMonitor<EmailOptions> options,
    TimeProvider time,
    ILogger<EmailDispatcher> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ReportConfiguration();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await ProcessBatchAsync(stoppingToken) == BatchSize)
                {
                    // Keep draining while full batches come back.
                }

                await signal.WaitAsync(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Typically the database is briefly unavailable; try again shortly.
                logger.LogError(ex, "Email dispatcher loop failed; retrying in {Delay}", PollInterval);
                await Task.Delay(PollInterval, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    private async Task<int> ProcessBatchAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<EmailOutboxRepository>();
        var transport = scope.ServiceProvider.GetRequiredService<IEmailTransport>();
        var batch = await outbox.ClaimDueAsync(BatchSize, stoppingToken);

        foreach (var email in batch)
        {
            if (!options.CurrentValue.Enabled)
            {
                await outbox.MarkDisabledAsync(email.Id, stoppingToken);
                continue;
            }

            try
            {
                var result = await transport.SendAsync(email.ToMessage(), stoppingToken);
                await outbox.MarkDeliveredAsync(email.Id, result, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw; // Left in "Sending"; picked up again after restart.
            }
            catch (Exception ex)
            {
                var retryAt = IsTransient(ex) && email.Attempts <= RetryDelays.Length
                    ? time.GetUtcNow().UtcDateTime + RetryDelays[email.Attempts - 1]
                    : (DateTime?)null;
                var method = options.CurrentValue.ResolveDeliveryMethod();
                var hint = (ex as EmailDeliveryException)?.Hint;
                var error = hint is null ? ex.Message : $"{ex.Message} — {hint}";
                await outbox.MarkFailedAsync(email.Id, method, error, retryAt, CancellationToken.None);

                if (retryAt is null)
                {
                    logger.LogError(ex, "Giving up on email {EmailId} \"{Subject}\" to {Recipients} after {Attempts} attempts",
                        email.Id, email.Subject, email.ToAddresses, email.Attempts);
                }
                else
                {
                    logger.LogWarning(ex, "Sending email {EmailId} \"{Subject}\" failed (attempt {Attempt}); retrying at {RetryAt:u}",
                        email.Id, email.Subject, email.Attempts, retryAt);
                }
            }
        }

        return batch.Count;
    }

    /// <summary>Only network hiccups and temporary (4xx) refusals are worth retrying; wrong settings fail immediately.</summary>
    internal static bool IsTransient(Exception ex)
    {
        if (ex is EmailDeliveryException { Retryable: true })
        {
            return true;
        }

        var cause = ex is EmailDeliveryException { InnerException: { } inner } ? inner : ex;
        return cause is SocketException or TimeoutException or IOException or SmtpProtocolException
                   or ServiceNotConnectedException or OperationCanceledException
               || cause is SmtpCommandException command && (int)command.StatusCode is >= 400 and < 500;
    }

    private void ReportConfiguration()
    {
        var report = diagnostics.GetReport();
        logger.LogInformation("Email: {Summary}", report.Summary);
        foreach (var warning in report.Warnings)
        {
            logger.LogWarning("Email configuration: {Warning}", warning);
        }
    }
}
