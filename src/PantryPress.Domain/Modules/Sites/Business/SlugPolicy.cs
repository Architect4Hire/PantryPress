using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Business;

/// <summary>
/// What slug a site may take (SITE-002; PP2.8): the shape <see cref="SiteSlugs"/> checks for lookups, the rules a host
/// name adds, and the <see cref="ReservedSlugs"/> list. Comparison is case-insensitive and ASCII only: a slug is checked
/// for ASCII before it is lower-cased, so no look-alike letter becomes a real one. Whether a site already holds the slug
/// is the caller's question, and is answered with the same <see cref="SiteErrors.SlugUnavailable"/> (PP2.10).
/// </summary>
public static class SlugPolicy
{
    /// <summary>The request field a refused shape is reported against.</summary>
    public const string Field = "slug";

    public const string InvalidMessage =
        "Choose an address of 3 to 40 lowercase letters, numbers, and single hyphens, starting and ending with a letter or number.";

    /// <summary>The slug's stored form when a site may take it; otherwise why not.</summary>
    public static Result<string> Check(string? requested)
    {
        if (!IsWellFormed(requested, out var slug))
        {
            return Result<string>.Failure(Error.Validation(new Dictionary<string, string[]> { [Field] = [InvalidMessage] }));
        }

        return ReservedSlugs.IsReserved(slug) ? Result<string>.Failure(SiteErrors.SlugUnavailable) : Result<string>.Success(slug);
    }

    /// <summary>
    /// True when <paramref name="requested"/> has a slug's shape and is a host name's label. No hyphen at either end, which
    /// a host name may not have, and no two together: <c>xn--</c> marks an internationalized label, which browsers show in
    /// Unicode, so an ASCII slug could otherwise be displayed as a look-alike of another site's address (RFC 5891). Nor the
    /// shape of a site id (8-4-4-4-12 hex), which begins every site's cache key (PP2.6) and will begin its blob prefixes
    /// and purge tags: no blogger loses anything, and a slug never reads as another site's id.
    /// </summary>
    public static bool IsWellFormed(string? requested, out string slug)
    {
        if (!SiteSlugs.TryNormalize(requested, out slug)
            || slug[0] == '-'
            || slug[^1] == '-'
            || slug.Contains("--", StringComparison.Ordinal)
            || Guid.TryParseExact(slug, "D", out _))
        {
            slug = string.Empty;
            return false;
        }

        return true;
    }
}
