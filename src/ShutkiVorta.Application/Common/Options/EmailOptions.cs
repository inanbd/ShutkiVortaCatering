namespace ShutkiVorta.Application.Common.Options;

/// <summary>Outgoing email configuration, bound from the "Email" section of appsettings.json.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Master switch. When false, emails are recorded in the email log but not sent.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// "Auto" (default): send through SMTP as soon as <see cref="SmtpSettings.Host"/> is filled in, otherwise save to the pickup folder.
    /// "Smtp": always send through the SMTP server. "PickupDirectory": never send; write .eml/.html files (development).
    /// </summary>
    public string DeliveryMethod { get; set; } = EmailDeliveryMethods.Auto;

    public string PickupDirectory { get; set; } = "App_Data/mail";
    public string FromName { get; set; } = "Shutki Vorta Catering";
    public string FromAddress { get; set; } = "no-reply@example.com";
    public string? ReplyToAddress { get; set; }

    /// <summary>Administrators who receive new-order and inquiry notifications.</summary>
    public List<string> AdminRecipients { get; set; } = [];

    public bool SendCustomerStatusUpdates { get; set; } = true;

    public SmtpSettings Smtp { get; set; } = new();

    /// <summary>The delivery method actually in effect after resolving "Auto".</summary>
    public string ResolveDeliveryMethod()
    {
        if (string.Equals(DeliveryMethod, EmailDeliveryMethods.Smtp, StringComparison.OrdinalIgnoreCase))
        {
            return EmailDeliveryMethods.Smtp;
        }

        if (string.Equals(DeliveryMethod, EmailDeliveryMethods.PickupDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return EmailDeliveryMethods.PickupDirectory;
        }

        // "Auto" or anything unrecognised: use SMTP once a real server has been configured.
        return Smtp.IsConfigured ? EmailDeliveryMethods.Smtp : EmailDeliveryMethods.PickupDirectory;
    }
}

public sealed class SmtpSettings
{
    /// <summary>SMTP server host name, e.g. "smtp.gmail.com". Leave empty to keep emails in the pickup folder.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>587 (STARTTLS, recommended), 465 (SSL/TLS) or 25.</summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// "Auto" (default: SSL/TLS on port 465, otherwise STARTTLS when offered), "StartTls", "SslOnConnect" or "None".
    /// Obvious port/security mismatches (STARTTLS on 465, SSL on 587/25) are corrected automatically.
    /// </summary>
    public string Security { get; set; } = "Auto";

    public string? UserName { get; set; }
    public string? Password { get; set; }
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Accept self-signed or otherwise untrusted server certificates. Only for your own mail server on a trusted network.</summary>
    public bool AcceptInvalidCertificates { get; set; }

    /// <summary>Check whether the server certificate was revoked. Turn off if revocation lists cannot be reached from your host.</summary>
    public bool CheckCertificateRevocation { get; set; } = true;

    /// <summary>Optional HELO/EHLO name. Set it if the server rejects the machine's own host name.</summary>
    public string? LocalDomain { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !IsPlaceholder(Host);

    public static bool IsPlaceholder(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.EndsWith("example.com", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("example.org", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("example.net", StringComparison.OrdinalIgnoreCase));
}

public static class EmailDeliveryMethods
{
    public const string Auto = "Auto";
    public const string Smtp = "Smtp";
    public const string PickupDirectory = "PickupDirectory";
}
