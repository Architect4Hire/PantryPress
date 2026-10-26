using System.Text;
using System.Text.RegularExpressions;

namespace PantryPress.Domain.Managers.Tenancy;

/// <summary>
/// A cache key that belongs to one site (tenancy.md rule 8; ARCH-015, PSEC-007; PP2.6): <c>{siteId}:{area}:{part}:…</c>,
/// beginning with the site's id. There is no way to name a site here: <see cref="For"/> takes the one
/// <see cref="ISiteContext"/> a resolver entered - a stand-in is refused, as the database context refuses one - so an
/// entry can never be filed under a site the work is not on. Code never builds a cache key by hand.
/// </summary>
/// <remarks>
/// <para>
/// The key is unambiguous by construction. The site id is always 36 lowercase characters, and every key that is not a
/// site's begins <c>platform:</c> (<see cref="PlatformCacheKey"/>), so a slug or host shaped like a site id is still a
/// part, never a prefix. The area cannot contain the separator. Every part is non-empty, well-formed text, with <c>%</c>
/// and <c>:</c> percent-escaped, so no two lists of parts give the same key - in .NET or in the UTF-8 the cache stores -
/// and no part can reach into another site's or another area's keys.
/// </para>
/// <para>
/// No <c>{…}</c> braces: in clustered Redis they would pin every key of a site to one shard, and a site that goes viral
/// would load that shard alone. The key is capped at <see cref="MaxBytes"/> UTF-8 bytes; a long value a reader supplied
/// is hashed by its caller first, and a personal one (an email, an address) is hashed at any length. Keys compare
/// ordinally. A <c>default</c> key has no value, and whatever accepts one must refuse it.
/// </para>
/// </remarks>
public readonly partial record struct SiteCacheKey
{
    public const int MaxBytes = 1024;

    private SiteCacheKey(SiteId site, string value)
    {
        Site = site;
        Value = value;
    }

    /// <summary>The site the key belongs to.</summary>
    public SiteId Site { get; }

    /// <summary>The key as the cache stores it.</summary>
    public string Value { get; }

    /// <summary>
    /// The key for <paramref name="area"/> - what is cached, such as <c>page-data</c> - and its parts, under the site
    /// <paramref name="context"/> works on. Throws when there is no site: caching site data without one is a bug.
    /// </summary>
    public static SiteCacheKey For(ISiteContext context, string area, params ReadOnlySpan<string> parts)
    {
        var site = SiteOf(context);
        var key = new StringBuilder(PrefixOf(site));
        CacheKeyText.Append(key, area, parts);
        return new SiteCacheKey(site, key.ToString());
    }

    /// <summary>
    /// What every key of the site <paramref name="context"/> works on begins with, separator included, for clearing all of
    /// that site's entries at once. Throws when there is no site.
    /// </summary>
    public static string Prefix(ISiteContext context) => PrefixOf(SiteOf(context));

    /// <summary>
    /// What every key of one <paramref name="area"/> of that site begins with, separator included, so clearing
    /// <c>page</c> never reaches <c>page-data</c>.
    /// </summary>
    public static string Prefix(ISiteContext context, string area)
    {
        var key = new StringBuilder(PrefixOf(SiteOf(context)));
        CacheKeyText.Append(key, area, []);
        return key.Append(CacheKeyText.Separator).ToString();
    }

    public override string ToString() => Value;

    private static SiteId SiteOf(ISiteContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ResolvedSiteContext.Require(context).Required;
    }

    private static string PrefixOf(SiteId site) => site.Value.ToString("D") + CacheKeyText.Separator;
}

/// <summary>
/// A cache key that belongs to no site (PP2.6): <c>platform:{area}:{part}:…</c>, for what is cached before a site is
/// known or across all of them - the slug and host resolution caches (tenancy.md, "Tenant root"), per-address and
/// global rate-limit counters. Its fixed first word can never read as a site id, so no platform entry lands in a site's
/// key space, whatever slug or host it is keyed by. Parts follow the same rules as <see cref="SiteCacheKey"/>.
/// </summary>
public readonly record struct PlatformCacheKey
{
    public const string Namespace = "platform";

    private PlatformCacheKey(string value) => Value = value;

    /// <summary>The key as the cache stores it.</summary>
    public string Value { get; }

    public static PlatformCacheKey For(string area, params ReadOnlySpan<string> parts)
    {
        var key = new StringBuilder(Namespace).Append(CacheKeyText.Separator);
        CacheKeyText.Append(key, area, parts);
        return new PlatformCacheKey(key.ToString());
    }

    public override string ToString() => Value;
}

/// <summary>The area and parts both kinds of key share: the shapes, the escaping, and the length limit.</summary>
internal static partial class CacheKeyText
{
    public const char Separator = ':';

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static void Append(StringBuilder key, string area, ReadOnlySpan<string> parts)
    {
        if (area is null || !AreaShape().IsMatch(area))
        {
            throw new ArgumentException(
                "A cache area is 1 to 40 lowercase letters, digits, or hyphens, starting with a letter.", nameof(area));
        }

        key.Append(area);
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
            {
                throw new ArgumentException("A cache key part is never empty.", nameof(parts));
            }

            key.Append(Separator);
            AppendEscaped(key, part);
        }

        // Redis stores the key as UTF-8, where a lone surrogate would become U+FFFD and two parts could meet; the limit
        // counts those bytes too.
        int bytes;
        try
        {
            bytes = StrictUtf8.GetByteCount(key.ToString());
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException("A cache key part is well-formed text.", nameof(parts));
        }

        if (bytes > SiteCacheKey.MaxBytes)
        {
            throw new ArgumentException($"A cache key is at most {SiteCacheKey.MaxBytes} bytes; hash a long part first.", nameof(parts));
        }
    }

    private static void AppendEscaped(StringBuilder key, string part)
    {
        foreach (var c in part)
        {
            _ = c switch
            {
                '%' => key.Append("%25"),
                Separator => key.Append("%3A"),
                _ => key.Append(c),
            };
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex AreaShape();
}
