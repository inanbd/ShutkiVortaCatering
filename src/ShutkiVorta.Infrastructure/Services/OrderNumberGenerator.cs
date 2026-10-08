using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using ShutkiVorta.Application.Common.Interfaces;

namespace ShutkiVorta.Infrastructure.Services;

internal sealed class OrderNumberGenerator : IOrderNumberGenerator
{
    // No 0/O, 1/I/L to keep numbers easy to read over the phone.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public string NewOrderNumber(DateTime businessNow) =>
        $"SV-{businessNow.ToString("yyMMdd", CultureInfo.InvariantCulture)}-{RandomSuffix()}";

    public string NewStandingOrderReference(DateTime businessNow) =>
        $"RO-{businessNow.ToString("yyMM", CultureInfo.InvariantCulture)}-{RandomSuffix()}";

    public string NewTrackingToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));

    private static string RandomSuffix()
    {
        Span<char> suffix = stackalloc char[4];
        for (var i = 0; i < suffix.Length; i++)
        {
            suffix[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(suffix);
    }
}
