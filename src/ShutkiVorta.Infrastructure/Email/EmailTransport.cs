using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Domain.Common;

namespace ShutkiVorta.Infrastructure.Email;

/// <summary>Delivers email using the method configured in Admin → Settings → Email (default "Auto").</summary>
internal sealed class EmailTransport(
    IOptionsMonitor<EmailOptions> options,
    IHostEnvironment environment,
    ILogger<EmailTransport> logger) : IEmailTransport
{
    public async Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = options.CurrentValue;
        var mime = BuildMimeMessage(message, settings);

        if (settings.ResolveDeliveryMethod() == EmailDeliveryMethods.Smtp)
        {
            var reply = await SendViaSmtpAsync(mime, settings.Smtp, cancellationToken);
            logger.LogInformation("Sent email \"{Subject}\" to {Recipients} via {Host}: {Reply}", message.Subject, string.Join(", ", message.To), settings.Smtp.Host, reply);
            return new EmailDeliveryResult(EmailDeliveryMethods.Smtp, ActuallySent: true, reply);
        }

        var path = await WriteToPickupDirectoryAsync(mime, message, settings, cancellationToken);
        logger.LogInformation("Saved email \"{Subject}\" to {Path} (pickup folder — not sent)", message.Subject, path);
        return new EmailDeliveryResult(EmailDeliveryMethods.PickupDirectory, ActuallySent: false, path);
    }

    public async Task<SmtpConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.CurrentValue;
        var smtp = settings.Smtp;
        var steps = new List<SmtpConnectionTestStep>();

        if (settings.ResolveDeliveryMethod() != EmailDeliveryMethods.Smtp)
        {
            return new SmtpConnectionTestResult(
                false,
                "No SMTP server is in use: emails are saved to the pickup folder and are not sent.",
                steps,
                string.IsNullOrWhiteSpace(smtp.Host) || !smtp.IsConfigured
                    ? "Fill in the SMTP server, port, user name and password in Admin → Settings → Email."
                    : "Set the delivery method to \"Automatic\" in Admin → Settings → Email (Advanced).");
        }

        var (security, correction) = SmtpSecurity.Resolve(smtp);
        if (correction is not null)
        {
            steps.Add(new SmtpConnectionTestStep("Settings", true, correction));
        }

        using var client = CreateClient(smtp);
        try
        {
            await client.ConnectAsync(smtp.Host, smtp.Port, security, cancellationToken);
            var tls = client.IsSecure ? $"encrypted with {client.SslProtocol}" : "NOT encrypted";
            steps.Add(new SmtpConnectionTestStep("Connect", true, $"Connected to {smtp.Host}:{smtp.Port} using {SmtpSecurity.Describe(security)} — {tls}."));

            if (!string.IsNullOrWhiteSpace(smtp.UserName))
            {
                await client.AuthenticateAsync(smtp.UserName, smtp.Password ?? string.Empty, cancellationToken);
                steps.Add(new SmtpConnectionTestStep("Sign in", true, $"Signed in as {smtp.UserName}."));
            }
            else
            {
                var offered = client.AuthenticationMechanisms.Count > 0 ? $" (the server offers sign-in: {string.Join(", ", client.AuthenticationMechanisms)})" : string.Empty;
                steps.Add(new SmtpConnectionTestStep("Sign in", true, $"Skipped — no UserName configured{offered}."));
            }

            await client.NoOpAsync(cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
            return new SmtpConnectionTestResult(true, "The mail server accepted the connection and sign-in. Use \"Send test email\" to try a real message.", steps, null);
        }
        catch (Exception ex)
        {
            var step = !client.IsConnected ? "Connect" : "Sign in";
            steps.Add(new SmtpConnectionTestStep(step, false, ex.Message));
            logger.LogWarning(ex, "SMTP connection test to {Host}:{Port} failed at {Step}", smtp.Host, smtp.Port, step);
            return new SmtpConnectionTestResult(false, $"{step} failed: {ex.Message}", steps, SmtpErrorHints.Explain(ex, smtp));
        }
    }

    private static MimeMessage BuildMimeMessage(EmailMessage message, EmailOptions settings)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        foreach (var to in message.To)
        {
            mime.To.Add(MailboxAddress.Parse(to));
        }

        var replyTo = message.ReplyTo ?? settings.ReplyToAddress;
        if (!string.IsNullOrWhiteSpace(replyTo))
        {
            mime.ReplyTo.Add(MailboxAddress.Parse(replyTo));
        }

        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();
        return mime;
    }

    private SmtpClient CreateClient(SmtpSettings smtp)
    {
        var client = new SmtpClient
        {
            Timeout = Math.Max(5, smtp.TimeoutSeconds) * 1000,
            CheckCertificateRevocation = smtp.CheckCertificateRevocation,
        };

        if (smtp.AcceptInvalidCertificates)
        {
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;
        }

        if (!string.IsNullOrWhiteSpace(smtp.LocalDomain))
        {
            client.LocalDomain = smtp.LocalDomain;
        }

        return client;
    }

    private async Task<string> SendViaSmtpAsync(MimeMessage mime, SmtpSettings smtp, CancellationToken cancellationToken)
    {
        var (security, _) = SmtpSecurity.Resolve(smtp);
        using var client = CreateClient(smtp);
        try
        {
            await client.ConnectAsync(smtp.Host, smtp.Port, security, cancellationToken);
            if (!string.IsNullOrWhiteSpace(smtp.UserName))
            {
                await client.AuthenticateAsync(smtp.UserName, smtp.Password ?? string.Empty, cancellationToken);
            }

            var reply = await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
            return string.IsNullOrWhiteSpace(reply) ? "accepted" : reply.Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw new EmailDeliveryException(ex.Message, SmtpErrorHints.Explain(ex, smtp), ex);
        }
    }

    /// <summary>Development mode: saves each email as .eml (open in any mail client) plus .html (open in a browser).</summary>
    private async Task<string> WriteToPickupDirectoryAsync(MimeMessage mime, EmailMessage message, EmailOptions settings, CancellationToken cancellationToken)
    {
        var directory = Path.IsPathRooted(settings.PickupDirectory)
            ? settings.PickupDirectory
            : Path.Combine(environment.ContentRootPath, settings.PickupDirectory);
        Directory.CreateDirectory(directory);

        var slug = SlugGenerator.Generate(message.Subject);
        var baseName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}_{(slug.Length > 60 ? slug[..60] : slug)}_{Guid.NewGuid().ToString("N")[..6]}";

        await mime.WriteToAsync(Path.Combine(directory, baseName + ".eml"), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, baseName + ".html"), message.HtmlBody, cancellationToken);
        return Path.Combine(directory, baseName + ".eml");
    }
}
