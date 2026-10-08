using ShutkiVorta.Application.Common.Email;

namespace ShutkiVorta.Application.Common.Interfaces;

/// <summary>Queues an email for background delivery so web requests are never blocked by SMTP.</summary>
public interface IEmailService
{
    ValueTask QueueAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Sends an email immediately (used for diagnostics such as the admin "send test email" button).</summary>
public interface IEmailTransport
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
