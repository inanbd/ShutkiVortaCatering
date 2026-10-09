using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Options;

namespace ShutkiVorta.Infrastructure.Settings;

/// <summary>Decrypts the stored SMTP password whenever email options are bound.</summary>
internal sealed class EmailSecretsPostConfigure(SettingsSecretProtector protector, ILogger<EmailSecretsPostConfigure> logger) : IPostConfigureOptions<EmailOptions>
{
    public void PostConfigure(string? name, EmailOptions options)
    {
        if (!SettingsSecretProtector.IsProtected(options.Smtp.Password))
        {
            return;
        }

        if (protector.TryUnprotect(options.Smtp.Password, out var password))
        {
            options.Smtp.Password = password;
            return;
        }

        logger.LogWarning("The saved SMTP password cannot be decrypted (the data-protection keys changed). Enter it again in Admin → Settings → Email.");
        options.Smtp.Password = null;
        options.Smtp.PasswordUnreadable = true;
    }
}
