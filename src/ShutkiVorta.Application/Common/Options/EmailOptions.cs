namespace ShutkiVorta.Application.Common.Options;

/// <summary>Outgoing email configuration, bound from the "Email" section of appsettings.json.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Master switch. When false, emails are logged but not sent.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>"Smtp" to send through an SMTP server, or "PickupDirectory" to write .eml/.html files (development).</summary>
    public string DeliveryMethod { get; set; } = EmailDeliveryMethods.PickupDirectory;

    public string PickupDirectory { get; set; } = "App_Data/mail";
    public string FromName { get; set; } = "Shutki Vorta Catering";
    public string FromAddress { get; set; } = "no-reply@example.com";
    public string? ReplyToAddress { get; set; }

    /// <summary>Administrators who receive new-order and inquiry notifications.</summary>
    public List<string> AdminRecipients { get; set; } = [];

    public bool SendCustomerStatusUpdates { get; set; } = true;

    public SmtpSettings Smtp { get; set; } = new();
}

public sealed class SmtpSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 587;

    /// <summary>None, Auto, SslOnConnect or StartTls.</summary>
    public string Security { get; set; } = "StartTls";

    public string? UserName { get; set; }
    public string? Password { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
}

public static class EmailDeliveryMethods
{
    public const string Smtp = "Smtp";
    public const string PickupDirectory = "PickupDirectory";
}
