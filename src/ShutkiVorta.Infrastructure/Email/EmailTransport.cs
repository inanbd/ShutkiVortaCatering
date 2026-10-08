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

/// <summary>Sends email using the method configured in appsettings.json ("Email:DeliveryMethod").</summary>
internal sealed class EmailTransport(
    IOptionsMonitor<EmailOptions> options,
    IHostEnvironment environment,
    ILogger<EmailTransport> logger) : IEmailTransport
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = options.CurrentValue;
        if (!settings.Enabled)
        {
            logger.LogInformation("Email disabled; skipped \"{Subject}\" to {Recipients}", message.Subject, string.Join(", ", message.To));
            return;
        }

        var mime = BuildMimeMessage(message, settings);

        if (string.Equals(settings.DeliveryMethod, EmailDeliveryMethods.Smtp, StringComparison.OrdinalIgnoreCase))
        {
            await SendViaSmtpAsync(mime, settings.Smtp, cancellationToken);
        }
        else
        {
            await WriteToPickupDirectoryAsync(mime, message, settings, cancellationToken);
        }

        logger.LogInformation("Sent email \"{Subject}\" to {Recipients} via {Method}", message.Subject, string.Join(", ", message.To), settings.DeliveryMethod);
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

    private static async Task SendViaSmtpAsync(MimeMessage mime, SmtpSettings smtp, CancellationToken cancellationToken)
    {
        var security = Enum.TryParse<SecureSocketOptions>(smtp.Security, ignoreCase: true, out var parsed) ? parsed : SecureSocketOptions.Auto;

        using var client = new SmtpClient { Timeout = Math.Max(5, smtp.TimeoutSeconds) * 1000 };
        await client.ConnectAsync(smtp.Host, smtp.Port, security, cancellationToken);
        if (!string.IsNullOrWhiteSpace(smtp.UserName))
        {
            await client.AuthenticateAsync(smtp.UserName, smtp.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    /// <summary>Development mode: saves each email as .eml (open in any mail client) plus .html (open in a browser).</summary>
    private async Task WriteToPickupDirectoryAsync(MimeMessage mime, EmailMessage message, EmailOptions settings, CancellationToken cancellationToken)
    {
        var directory = Path.IsPathRooted(settings.PickupDirectory)
            ? settings.PickupDirectory
            : Path.Combine(environment.ContentRootPath, settings.PickupDirectory);
        Directory.CreateDirectory(directory);

        var slug = SlugGenerator.Generate(message.Subject);
        var baseName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}_{(slug.Length > 60 ? slug[..60] : slug)}_{Guid.NewGuid().ToString("N")[..6]}";

        await mime.WriteToAsync(Path.Combine(directory, baseName + ".eml"), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, baseName + ".html"), message.HtmlBody, cancellationToken);
    }
}
