using MailKit.Security;
using ShutkiVorta.Application.Common.Options;

namespace ShutkiVorta.Infrastructure.Email;

/// <summary>Turns the configured "Security" text into a MailKit option, fixing the classic port/security mix-ups.</summary>
internal static class SmtpSecurity
{
    public static (SecureSocketOptions Option, string? Correction) Resolve(SmtpSettings smtp)
    {
        var requested = (smtp.Security ?? string.Empty).Trim().Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant() switch
        {
            "" or "auto" or "default" => SecureSocketOptions.Auto,
            "none" or "off" or "false" or "plain" => SecureSocketOptions.None,
            "starttls" or "tls" or "starttlswhenavailable" => SecureSocketOptions.StartTls,
            "ssl" or "sslonconnect" or "implicit" or "implicittls" or "ssltls" => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.Auto,
        };

        // Port 465 always speaks SSL/TLS from the first byte; STARTTLS there just times out.
        if (smtp.Port == 465 && requested is SecureSocketOptions.StartTls or SecureSocketOptions.StartTlsWhenAvailable)
        {
            return (SecureSocketOptions.SslOnConnect, "Port 465 requires SSL/TLS on connect, so \"SslOnConnect\" is used instead of \"StartTls\".");
        }

        // Ports 587 and 25 start in plain text and upgrade with STARTTLS; SSL-on-connect fails there.
        if (smtp.Port is 587 or 25 && requested == SecureSocketOptions.SslOnConnect)
        {
            return (SecureSocketOptions.StartTls, $"Port {smtp.Port} uses STARTTLS, so \"StartTls\" is used instead of \"SslOnConnect\".");
        }

        // "Auto" must never fall back to plain text on the network: an attacker could strip the STARTTLS offer and read
        // the password. Only a mail server on this machine may skip encryption when it doesn't offer it.
        if (requested == SecureSocketOptions.Auto)
        {
            return (smtp.Port == 465 ? SecureSocketOptions.SslOnConnect
                : IsLocalHost(smtp.Host) ? SecureSocketOptions.StartTlsWhenAvailable
                : SecureSocketOptions.StartTls, null);
        }

        return (requested, null);
    }

    private static bool IsLocalHost(string? host) =>
        host?.Trim().ToLowerInvariant() is "localhost" or "127.0.0.1" or "::1" or "[::1]";

    public static string Describe(SecureSocketOptions option) => option switch
    {
        SecureSocketOptions.None => "no encryption",
        SecureSocketOptions.SslOnConnect => "SSL/TLS",
        SecureSocketOptions.StartTls => "STARTTLS (required)",
        SecureSocketOptions.StartTlsWhenAvailable => "STARTTLS when offered",
        _ => "automatic",
    };
}
