using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Emails;

namespace ShutkiVorta.Infrastructure.Email;

/// <summary>Plain-English report of how email is configured, flagging the settings that stop mail from arriving.</summary>
internal sealed class EmailDiagnostics(IOptionsMonitor<EmailOptions> options, IHostEnvironment environment) : IEmailDiagnostics
{
    public EmailConfigurationReport GetReport()
    {
        var o = options.CurrentValue;
        var smtp = o.Smtp;
        var effective = o.ResolveDeliveryMethod();
        var warnings = new List<string>();

        if (!new[] { EmailDeliveryMethods.Auto, EmailDeliveryMethods.Smtp, EmailDeliveryMethods.PickupDirectory }
                .Contains(o.DeliveryMethod, StringComparer.OrdinalIgnoreCase))
        {
            warnings.Add($"The delivery method \"{o.DeliveryMethod}\" is not recognised; it is treated as \"Automatic\". Choose one from the list (Email → Advanced).");
        }

        if (!o.Enabled)
        {
            warnings.Add("Email is switched off (\"Send emails\" is unticked): nothing is sent.");
        }

        if (effective == EmailDeliveryMethods.PickupDirectory)
        {
            var folder = Path.IsPathRooted(o.PickupDirectory) ? o.PickupDirectory : Path.Combine(environment.ContentRootPath, o.PickupDirectory);
            warnings.Add(string.Equals(o.DeliveryMethod, EmailDeliveryMethods.PickupDirectory, StringComparison.OrdinalIgnoreCase)
                ? $"The delivery method is \"Never send; save to a folder\": emails are saved as files in {folder} and are NOT sent. Change it to \"Automatic\" (Email → Advanced) to send real email."
                : $"No SMTP server is set (\"SMTP server\" is empty or a placeholder): emails are saved as files in {folder} and are NOT sent.");
        }
        else
        {
            if (!smtp.IsConfigured)
            {
                warnings.Add($"The delivery method is \"Always send through SMTP\" but the SMTP server (\"{smtp.Host}\") is empty or a placeholder.");
            }

            var (security, correction) = SmtpSecurity.Resolve(smtp);
            if (correction is not null)
            {
                warnings.Add(correction);
            }

            if (security == MailKit.Security.SecureSocketOptions.None && !string.IsNullOrWhiteSpace(smtp.UserName))
            {
                warnings.Add("Encryption is \"None\": your SMTP password and every email travel unencrypted. Use \"Automatic\" unless the mail server runs on this machine.");
            }

            if (smtp.Port == 25)
            {
                warnings.Add("Port 25 is blocked by most cloud and home internet providers. Prefer port 587 (STARTTLS) or 465 (SSL/TLS).");
            }

            if (smtp.PasswordUnreadable)
            {
                warnings.Add("The saved SMTP password can no longer be read (the server's data-protection keys changed). Enter the password again under Email → Mail server.");
            }
            else if (!string.IsNullOrWhiteSpace(smtp.UserName) && string.IsNullOrEmpty(smtp.Password))
            {
                warnings.Add("A user name is set but no password has been saved. Enter the password under Email → Mail server.");
            }

            if (!string.IsNullOrWhiteSpace(smtp.UserName) && smtp.UserName.Contains('@')
                && !string.Equals(smtp.UserName, o.FromAddress, StringComparison.OrdinalIgnoreCase)
                && (smtp.Host.Contains("gmail", StringComparison.OrdinalIgnoreCase)
                    || smtp.Host.Contains("office365", StringComparison.OrdinalIgnoreCase)
                    || smtp.Host.Contains("outlook", StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add($"Gmail and Microsoft 365 only send from the signed-in mailbox: set the sender address to {smtp.UserName} (or an approved alias).");
            }

            if (smtp.AcceptInvalidCertificates)
            {
                warnings.Add("\"Accept untrusted certificates\" is on: the server's certificate is not verified. Use only for your own mail server.");
            }
        }

        if (SmtpSettings.IsPlaceholder(o.FromAddress))
        {
            warnings.Add($"The sender address is still a placeholder ({o.FromAddress}). Most mail servers reject or spam-folder mail from example.com.");
        }

        var admins = o.AdminRecipients.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        if (admins.Count == 0)
        {
            warnings.Add("Nobody is set under \"Notify about new orders\": nobody is emailed about new orders.");
        }
        else if (admins.All(SmtpSettings.IsPlaceholder))
        {
            warnings.Add($"\"Notify about new orders\" still points to a placeholder ({string.Join(", ", admins)}): new-order notifications go nowhere.");
        }

        var summary = !o.Enabled
            ? "Email is switched off."
            : effective == EmailDeliveryMethods.Smtp
                ? $"Sending real email through {smtp.Host}:{smtp.Port} ({SmtpSecurity.Describe(SmtpSecurity.Resolve(smtp).Option)}){(string.IsNullOrWhiteSpace(smtp.UserName) ? " without sign-in" : $" as {smtp.UserName}")}."
                : "NOT sending email — messages are only saved to a folder on the server.";

        return new EmailConfigurationReport(o.Enabled, o.DeliveryMethod, effective, summary, warnings);
    }
}
