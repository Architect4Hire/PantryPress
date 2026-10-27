using System.Globalization;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Business;

/// <summary>
/// The rules for a site's own text fields - its name, tagline, and locale - shared by the validators that accept them
/// (customization spec, <c>brand.siteName</c> and <c>brand.tagline</c>). Plain text: it is encoded wherever it is shown,
/// never sanitized into markup.
/// </summary>
public static class SiteText
{
    /// <summary>The locale a site gets when its creator gives none.</summary>
    public const string DefaultLocale = "en-US";

    /// <summary>
    /// True when <paramref name="value"/> is one line of plain text: no control character, no line or paragraph
    /// separator, and no unpaired surrogate, which no encoder can write out.
    /// </summary>
    public static bool IsSingleLine(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(c)
                || char.IsControl(c)
                || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when <paramref name="value"/> is a BCP 47 tag in its usual case: a language of two or three letters, an
    /// optional four-letter script, and an optional two-letter or three-digit region (<c>en</c>, <c>en-US</c>,
    /// <c>zh-Hant-TW</c>, <c>es-419</c>). The shape only: which locales the public site renders comes with the prompts
    /// that render them.
    /// </summary>
    public static bool IsLocale(string? value)
    {
        if (value is null || value.Length > SiteLimits.MaxLocaleLength)
        {
            return false;
        }

        var parts = value.Split('-');
        if (parts.Length > 3 || !IsLower(parts[0], 2, 3))
        {
            return false;
        }

        var next = 1;
        if (next < parts.Length && parts[next].Length == 4)
        {
            var script = parts[next];
            if (!char.IsAsciiLetterUpper(script[0]) || !IsLower(script[1..], 3, 3))
            {
                return false;
            }

            next++;
        }

        if (next < parts.Length)
        {
            var region = parts[next];
            if (!(region.Length == 2 && region.All(char.IsAsciiLetterUpper))
                && !(region.Length == 3 && region.All(char.IsAsciiDigit)))
            {
                return false;
            }

            next++;
        }

        return next == parts.Length;
    }

    private static bool IsLower(string part, int min, int max) =>
        part.Length >= min && part.Length <= max && part.All(char.IsAsciiLetterLower);
}
