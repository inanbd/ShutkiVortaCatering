using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Interfaces;

namespace ShutkiVorta.Infrastructure.Email;

/// <summary>Background service that sends queued emails, retrying transient failures.</summary>
internal sealed class EmailDispatcher(EmailQueue queue, IServiceScopeFactory scopes, ILogger<EmailDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];
    private static readonly TimeSpan ShutdownFlushTimeout = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
            {
                await SendWithRetryAsync(message, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }

        await FlushRemainingAsync();
    }

    /// <summary>On shutdown, give already-queued emails (e.g. an order confirmation) a short chance to go out.</summary>
    private async Task FlushRemainingAsync()
    {
        using var timeout = new CancellationTokenSource(ShutdownFlushTimeout);
        while (!timeout.IsCancellationRequested && queue.Reader.TryRead(out var message))
        {
            await SendOnceAsync(message, timeout.Token);
        }
    }

    private async Task SendWithRetryAsync(EmailMessage message, CancellationToken stoppingToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await SendAsync(message, stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (attempt < RetryDelays.Length)
            {
                logger.LogWarning(ex, "Sending email \"{Subject}\" failed (attempt {Attempt}); retrying", message.Subject, attempt + 1);
                try
                {
                    await Task.Delay(RetryDelays[attempt], stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    await SendOnceAsync(message, CancellationToken.None);
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Giving up on email \"{Subject}\" to {Recipients}", message.Subject, string.Join(", ", message.To));
                return;
            }
        }
    }

    private async Task SendOnceAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not send email \"{Subject}\" to {Recipients} during shutdown", message.Subject, string.Join(", ", message.To));
        }
    }

    private async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var transport = scope.ServiceProvider.GetRequiredService<IEmailTransport>();
        await transport.SendAsync(message, cancellationToken);
    }
}
