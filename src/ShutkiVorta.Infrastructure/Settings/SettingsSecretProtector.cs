using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace ShutkiVorta.Infrastructure.Settings;

/// <summary>
/// Encrypts secret settings (the SMTP password) with ASP.NET Core Data Protection before they are stored in the database.
/// The keys live in DataProtection:KeysPath (App_Data/keys by default): if they are lost, secrets must be entered again.
/// </summary>
internal sealed class SettingsSecretProtector(IDataProtectionProvider provider)
{
    public const string Prefix = "enc:v1:";

    private readonly IDataProtector _protector = provider.CreateProtector("ShutkiVorta.Settings.Secrets.v1");

    public static bool IsProtected(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    public string Protect(string plainText) => Prefix + _protector.Protect(plainText);

    /// <summary>Plain values (e.g. from appsettings.json before the first import) are returned unchanged.</summary>
    public bool TryUnprotect(string? stored, out string? plainText)
    {
        if (!IsProtected(stored))
        {
            plainText = stored;
            return true;
        }

        try
        {
            plainText = _protector.Unprotect(stored![Prefix.Length..]);
            return true;
        }
        catch (CryptographicException)
        {
            plainText = null;
            return false;
        }
    }
}
