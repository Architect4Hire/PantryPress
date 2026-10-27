using System.Buffers.Text;
using System.Text;

namespace PantryPress.Domain.Modules.Sites.Business;

/// <summary>
/// The cursor of the caller's site list (api-contract.md: cursor-based pagination): the last slug of a page, opaque to the
/// client so its shape can change. A slug is unique and never changes inside a page's lifetime, so a page after it
/// neither repeats nor skips a site.
/// </summary>
public static class SiteListCursor
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 100;

    /// <summary>The cursor that continues after <paramref name="slug"/>, a slug in stored form.</summary>
    public static string After(string slug) => Base64Url.EncodeToString(Encoding.ASCII.GetBytes(slug));

    /// <summary>
    /// The slug a cursor continues after, when it is one this made: base64url of a slug's shape. Anything else - another
    /// encoding, a slug's look-alike, junk - is not a cursor.
    /// </summary>
    public static bool TryRead(string? cursor, out string slug)
    {
        slug = string.Empty;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 64 || !Base64Url.IsValid(cursor))
        {
            return false;
        }

        var text = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(cursor));
        if (!SiteSlugs.TryNormalize(text, out var stored)
            || !string.Equals(stored, text, StringComparison.Ordinal)
            || !string.Equals(After(stored), cursor, StringComparison.Ordinal))
        {
            return false;
        }

        slug = stored;
        return true;
    }
}
