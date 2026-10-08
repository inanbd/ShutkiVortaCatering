using System.Globalization;
using System.Text;

namespace ShutkiVorta.Domain.Common;

/// <summary>Builds lowercase, hyphenated, ASCII-only URL slugs (e.g. "Loitta Shutki Vorta" → "loitta-shutki-vorta").</summary>
public static class SlugGenerator
{
    public const int MaxLength = 120;

    public static string Generate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousWasHyphen = false;

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
                previousWasHyphen = false;
            }
            else if (c == '&')
            {
                if (!previousWasHyphen && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append("and-");
                previousWasHyphen = true;
            }
            else if (!previousWasHyphen && builder.Length > 0)
            {
                builder.Append('-');
                previousWasHyphen = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].TrimEnd('-');
        }

        return slug;
    }

    public static bool IsValid(string? slug) =>
        !string.IsNullOrEmpty(slug) && slug.Length <= MaxLength && Generate(slug) == slug;
}
