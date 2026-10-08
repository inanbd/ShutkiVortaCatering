using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using ShutkiVorta.Application.Common.Options;

namespace ShutkiVorta.Infrastructure.Email;

/// <summary>Maps MailKit/socket exceptions to plain-English advice so the owner can tell a mail-server problem from a code problem.</summary>
internal static class SmtpErrorHints
{
    public static string Explain(Exception ex, SmtpSettings smtp)
    {
        var message = ex.Message;
        var target = $"{smtp.Host}:{smtp.Port}";

        return ex switch
        {
            SslHandshakeException when message.Contains("revocation", StringComparison.OrdinalIgnoreCase) =>
                "The server's certificate revocation status could not be checked from this machine. Set \"CheckCertificateRevocation\": false in the Email:Smtp section.",
            SslHandshakeException when message.Contains("self-signed", StringComparison.OrdinalIgnoreCase)
                                     || message.Contains("could not be validated", StringComparison.OrdinalIgnoreCase) =>
                $"The mail server's TLS certificate is not trusted (self-signed, expired or for a different host name). Use the exact host name from your provider's certificate, " +
                "or — only for your own server on a trusted network — set \"AcceptInvalidCertificates\": true.",
            SslHandshakeException =>
                $"The secure connection to {target} failed. Use port 587 with \"Security\": \"StartTls\" or port 465 with \"SslOnConnect\" (\"Auto\" picks the right one).",
            AuthenticationException when message.Contains("not support", StringComparison.OrdinalIgnoreCase) =>
                "The server does not offer sign-in on this connection. Usually sign-in is only offered after encryption: use \"Security\": \"Auto\" or \"StartTls\".",
            AuthenticationException =>
                "The server rejected the user name or password. Gmail/Google Workspace need an App Password (with 2-Step Verification on); " +
                "Microsoft 365 needs \"Authenticated SMTP\" enabled for the mailbox; SendGrid uses the user name \"apikey\" and your API key as the password.",
            SmtpCommandException command when command.ErrorCode == SmtpErrorCode.SenderNotAccepted
                                           || message.Contains("SendAs", StringComparison.OrdinalIgnoreCase) =>
                $"The server refused the sender address. Set \"FromAddress\" to the mailbox you sign in with ({smtp.UserName ?? "your SMTP user"}) or to a sender/domain verified with your email provider.",
            SmtpCommandException command when command.ErrorCode == SmtpErrorCode.RecipientNotAccepted =>
                "The server refused the recipient address. Check for typos in the address (and in Email:AdminRecipients).",
            SmtpCommandException command when command.StatusCode == SmtpStatusCode.AuthenticationRequired =>
                "The server requires sign-in. Fill in \"UserName\" and \"Password\" in the Email:Smtp section.",
            SmtpCommandException command when (int)command.StatusCode is >= 500 and < 600 =>
                $"The server permanently rejected the message ({(int)command.StatusCode}). Common causes: unverified sender domain, missing SPF/DKIM, or sending limits on your plan.",
            SmtpCommandException =>
                "The server temporarily refused the message. It will be retried automatically; check your provider's sending limits if this continues.",
            SmtpProtocolException =>
                $"The server at {target} did not respond like an SMTP server. Check the port and the \"Security\" setting (465 = SslOnConnect, 587 = StartTls).",
            NotSupportedException when message.Contains("STARTTLS", StringComparison.OrdinalIgnoreCase) =>
                $"{target} does not offer STARTTLS. Use \"Security\": \"Auto\" — or \"None\" only for a server on your own trusted network.",
            SocketException socket when socket.SocketErrorCode == SocketError.HostNotFound || socket.SocketErrorCode == SocketError.TryAgain =>
                $"The host name \"{smtp.Host}\" could not be found. Check the spelling of Email:Smtp:Host.",
            SocketException socket when socket.SocketErrorCode == SocketError.ConnectionRefused =>
                $"Nothing is accepting connections on {target}. Check the port number with your email provider.",
            SocketException or TimeoutException or OperationCanceledException =>
                $"Could not reach {target} in time. Usually the port is blocked by a firewall — many cloud hosts (Azure, DigitalOcean, Google Cloud, AWS) block outbound ports 25/465/587 — " +
                "or the security setting does not match the port (STARTTLS on 465). Try port 587 with \"Auto\", ask your host to open SMTP, or use your provider's alternative port (e.g. 2525).",
            ServiceNotConnectedException or ServiceNotAuthenticatedException =>
                "The connection to the mail server dropped. It will be retried automatically.",
            _ => "Check the Email:Smtp settings against your email provider's SMTP instructions, then use \"Test connection\" in Admin → Settings.",
        };
    }
}
