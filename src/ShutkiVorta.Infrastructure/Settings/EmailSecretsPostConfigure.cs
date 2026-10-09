using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Options;

namespace ShutkiVorta.Infrastructure.Settings;

/// <summary>
/// Completes email options after they are bound from the database: decrypts the stored SMTP password and applies the
/// pickup folder, which is a server path and therefore comes from appsettings.json (Email:PickupDirectory), not the admin UI.
/// </summary>
internal sealed class EmailSecretsPostConfigure(
    SettingsSecretProtector protector,
    IConfiguration configuration,
    ILogger<EmailSecretsPostConfigure> logger) : IPostConfigureOptions<EmailOptions>
{
    private readonly Lock _gate = new();
    private (string Cipher, string? Plain)? _last;

    public void PostConfigure(string? name, EmailOptions options)
    {
        var pickup = configuration["Email:PickupDirectory"];
        if (!string.IsNullOrWhiteSpace(pickup))
        {
            options.PickupDirectory = pickup;
        }

        var stored = options.Smtp.Password;
        if (!SettingsSecretProtector.IsProtected(stored))
        {
            return;
        }

        var password = Decrypt(stored!);
        options.Smtp.Password = password;
        options.Smtp.PasswordUnreadable = password is null;
    }

    /// <summary>Options are re-bound on every request; decrypt (and warn) once per saved password, not every time.</summary>
    private string? Decrypt(string cipher)
    {
        lock (_gate)
        {
            if (_last is { } last && last.Cipher == cipher)
            {
                return last.Plain;
            }

            string? plain = protector.TryUnprotect(cipher, out var value) ? value : null;
            if (plain is null)
            {
                logger.LogWarning(
                    "The saved SMTP password cannot be decrypted (the data-protection keys changed). Emails are held and retried; enter the password again in Admin → Settings → Email.");
            }

            _last = (cipher, plain);
            return plain;
        }
    }
}
