using ShutkiVorta.Application.Common.Email;

namespace ShutkiVorta.Application.Common.Interfaces;

/// <summary>
/// Records an email in the outbox for background delivery so web requests are never blocked by SMTP.
/// Every email is kept in the email log with its delivery status and any error.
/// </summary>
public interface IEmailService
{
    ValueTask QueueAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Delivers an email immediately using the configured method (SMTP or pickup folder).</summary>
public interface IEmailTransport
{
    /// <summary>Sends the message. Throws on failure; the exception message is suitable for the email log.</summary>
    Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);

    /// <summary>Connects (and signs in) to the SMTP server without sending anything, reporting each step.</summary>
    Task<SmtpConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);
}
