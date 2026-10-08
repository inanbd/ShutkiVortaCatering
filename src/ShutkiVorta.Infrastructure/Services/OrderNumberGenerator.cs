using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using ShutkiVorta.Application.Common.Interfaces;

namespace ShutkiVorta.Infrastructure.Services;

internal sealed class OrderNumberGenerator : IOrderNumberGenerator
{
    // No 0/O, 1/I/L to keep numbers easy to read over the phone.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public string NewOrderNumber(DateTime businessNow)
    {
        Span<char> suffix = stackalloc char[4];
        for (var i = 0; i < suffix.Length; i++)
        {
            suffix[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"SV-{businessNow.ToString("yyMMdd", CultureInfo.InvariantCulture)}-{suffix}";
    }

    public string NewTrackingToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
}
