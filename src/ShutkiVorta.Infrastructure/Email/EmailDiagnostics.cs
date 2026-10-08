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
            warnings.Add($"Email:DeliveryMethod \"{o.DeliveryMethod}\" is not recognised; it is treated as \"Auto\". Use \"Auto\", \"Smtp\" or \"PickupDirectory\".");
        }

        if (!o.Enabled)
        {
            warnings.Add("Email is switched off (Email:Enabled = false): nothing is sent.");
        }

        if (effective == EmailDeliveryMethods.PickupDirectory)
        {
            var folder = Path.IsPathRooted(o.PickupDirectory) ? o.PickupDirectory : Path.Combine(environment.ContentRootPath, o.PickupDirectory);
            warnings.Add(string.Equals(o.DeliveryMethod, EmailDeliveryMethods.PickupDirectory, StringComparison.OrdinalIgnoreCase)
                ? $"Email:DeliveryMethod is \"PickupDirectory\": emails are saved as files in {folder} and are NOT sent. Change it to \"Auto\" or \"Smtp\" to send real email."
                : $"No SMTP server is configured (Email:Smtp:Host is empty or a placeholder): emails are saved as files in {folder} and are NOT sent.");
        }
        else
        {
            if (!smtp.IsConfigured)
            {
                warnings.Add($"Email:DeliveryMethod is \"Smtp\" but Email:Smtp:Host (\"{smtp.Host}\") is empty or a placeholder.");
            }

            var (_, correction) = SmtpSecurity.Resolve(smtp);
            if (correction is not null)
            {
                warnings.Add(correction);
            }

            if (smtp.Port == 25)
            {
                warnings.Add("Port 25 is blocked by most cloud and home internet providers. Prefer port 587 (STARTTLS) or 465 (SSL/TLS).");
            }

            if (!string.IsNullOrWhiteSpace(smtp.UserName) && string.IsNullOrEmpty(smtp.Password))
            {
                warnings.Add("Email:Smtp:UserName is set but Password is empty in this environment. User secrets only load in Development — on the server set the environment variable Email__Smtp__Password (or put it in appsettings.Production.json).");
            }

            if (!string.IsNullOrWhiteSpace(smtp.UserName) && smtp.UserName.Contains('@')
                && !string.Equals(smtp.UserName, o.FromAddress, StringComparison.OrdinalIgnoreCase)
                && (smtp.Host.Contains("gmail", StringComparison.OrdinalIgnoreCase)
                    || smtp.Host.Contains("office365", StringComparison.OrdinalIgnoreCase)
                    || smtp.Host.Contains("outlook", StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add($"Gmail and Microsoft 365 only send from the signed-in mailbox: set FromAddress to {smtp.UserName} (or an approved alias).");
            }

            if (smtp.AcceptInvalidCertificates)
            {
                warnings.Add("AcceptInvalidCertificates is on: the server's certificate is not verified. Use only for your own mail server.");
            }
        }

        if (SmtpSettings.IsPlaceholder(o.FromAddress))
        {
            warnings.Add($"FromAddress is still a placeholder ({o.FromAddress}). Most mail servers reject or spam-folder mail from example.com.");
        }

        var admins = o.AdminRecipients.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        if (admins.Count == 0)
        {
            warnings.Add("Email:AdminRecipients is empty: nobody is notified about new orders.");
        }
        else if (admins.All(SmtpSettings.IsPlaceholder))
        {
            warnings.Add($"Email:AdminRecipients still points to a placeholder ({string.Join(", ", admins)}): new-order notifications go nowhere.");
        }

        var summary = !o.Enabled
            ? "Email is switched off."
            : effective == EmailDeliveryMethods.Smtp
                ? $"Sending real email through {smtp.Host}:{smtp.Port} ({SmtpSecurity.Describe(SmtpSecurity.Resolve(smtp).Option)}){(string.IsNullOrWhiteSpace(smtp.UserName) ? " without sign-in" : $" as {smtp.UserName}")}."
                : "NOT sending email — messages are only saved to a folder on the server.";

        return new EmailConfigurationReport(o.Enabled, o.DeliveryMethod, effective, summary, warnings);
    }
}
