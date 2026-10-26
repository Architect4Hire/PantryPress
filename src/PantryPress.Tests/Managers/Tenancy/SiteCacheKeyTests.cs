using System.Text;
using PantryPress.Domain.Managers.Tenancy;

namespace PantryPress.Tests.Managers.Tenancy;

// PP2.6: a site cache key begins with its site's id and names only the site the work is on (tenancy.md rule 8; ARCH-015,
// PSEC-007). The collision tests prove the key is unambiguous: two look-alike sites never share a key or a prefix, no part
// can reach into another site's or another area's keys, and distinct inputs never give the same key - every key decodes
// back to exactly what made it.
public sealed class SiteCacheKeyTests
{
    private static readonly SiteId SiteA = new(Guid.Parse("0f7d8a1e-5c2b-4e6f-9a10-aabbccddeeff"));
    // A look-alike: the same id but for its last character.
    private static readonly SiteId SiteB = new(Guid.Parse("0f7d8a1e-5c2b-4e6f-9a10-aabbccddeef0"));

    [Fact]
    public void A_key_begins_with_the_id_of_the_site_the_work_is_on()
    {
        var key = SiteCacheKey.For(Working(SiteA), "page-data", "v3", "/recipes/pie");

        Assert.Equal("0f7d8a1e-5c2b-4e6f-9a10-aabbccddeeff:page-data:v3:/recipes/pie", key.Value);
        Assert.Equal(key.Value, key.ToString());
        Assert.Equal(SiteA, key.Site);
        Assert.Equal("0f7d8a1e-5c2b-4e6f-9a10-aabbccddeeff:", SiteCacheKey.Prefix(Working(SiteA)));
        Assert.StartsWith(SiteCacheKey.Prefix(Working(SiteA)), key.Value, StringComparison.Ordinal);
        Assert.Equal("0f7d8a1e-5c2b-4e6f-9a10-aabbccddeeff:home", SiteCacheKey.For(Working(SiteA), "home").Value);
    }

    [Fact]
    public void Look_alike_sites_never_share_a_key_or_a_prefix()
    {
        var a = SiteCacheKey.For(Working(SiteA), "page-data", "v1", "/recipes/supper");
        var b = SiteCacheKey.For(Working(SiteB), "page-data", "v1", "/recipes/supper");

        Assert.NotEqual(a, b);
        Assert.NotEqual(a.Value, b.Value);
        Assert.False(a.Value.StartsWith(SiteCacheKey.Prefix(Working(SiteB)), StringComparison.Ordinal));
        Assert.False(b.Value.StartsWith(SiteCacheKey.Prefix(Working(SiteA)), StringComparison.Ordinal));
        Assert.Equal(
            a.Value[SiteCacheKey.Prefix(Working(SiteA)).Length..],
            b.Value[SiteCacheKey.Prefix(Working(SiteB)).Length..]);
    }

    // Inputs built to collide if the separator or the escape were taken at face value.
    [Fact]
    public void Near_collisions_stay_apart()
    {
        var site = Working(SiteA);
        var keys = new[]
        {
            SiteCacheKey.For(site, "page-data", "a:b"),
            SiteCacheKey.For(site, "page-data", "a", "b"),
            SiteCacheKey.For(site, "page-data", "a%3Ab"),
            SiteCacheKey.For(site, "page-data", "a%b"),
            SiteCacheKey.For(site, "page-data", "a%25b"),
            SiteCacheKey.For(site, "page-data", "a:", "b"),
            SiteCacheKey.For(site, "page-data", "a", ":b"),
            SiteCacheKey.For(site, "page-data", "x"),
            SiteCacheKey.For(site, "page", "data:x"),
            SiteCacheKey.For(site, "page", "data", "x"),
            SiteCacheKey.For(site, "page-data", "X"),
            SiteCacheKey.For(site, "page-data"),
            SiteCacheKey.For(site, "page-data", ":"),
            SiteCacheKey.For(site, "page-data", "::"),
            SiteCacheKey.For(site, "page-data", ":", ":"),
        };

        Assert.Equal(keys.Length, keys.Select(k => k.Value).Distinct(StringComparer.Ordinal).Count());
    }

    // A part that spells another site's prefix - a path a reader typed, say - stays a part of this site's key.
    [Fact]
    public void A_part_cannot_reach_into_another_sites_keys()
    {
        var other = SiteCacheKey.Prefix(Working(SiteB)) + "page-data:v1";
        var key = SiteCacheKey.For(Working(SiteA), "page-data", other);

        Assert.StartsWith(SiteCacheKey.Prefix(Working(SiteA)), key.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(SiteCacheKey.Prefix(Working(SiteB)), key.Value, StringComparison.Ordinal);
        AssertDecodes(key, SiteA, "page-data", [other]);
    }

    // The property behind all of the above: over many inputs drawn from a small, hostile alphabet - so near misses are
    // common - distinct inputs never share a key, and every key decodes to exactly the input that made it.
    [Fact]
    public void Distinct_inputs_always_give_distinct_keys()
    {
        var random = new Random(2026_10_26);
        string[] alphabet = ["a", "b", "A", ":", "%", "3", "2", "5", "-", " ", "\t", "é", "😀", "/"];
        string[] areas = ["page", "page-data", "data", "p", "home-1"];
        SiteId[] sites = [SiteA, SiteB];
        var seen = new Dictionary<string, (SiteId Site, string Area, string[] Parts)>(StringComparer.Ordinal);

        for (var i = 0; i < 10_000; i++)
        {
            var site = sites[random.Next(sites.Length)];
            var area = areas[random.Next(areas.Length)];
            var parts = Enumerable.Range(0, random.Next(0, 4))
                .Select(_ => string.Concat(Enumerable.Range(0, random.Next(1, 5)).Select(_ => alphabet[random.Next(alphabet.Length)])))
                .ToArray();

            var key = SiteCacheKey.For(Working(site), area, parts);

            AssertDecodes(key, site, area, parts);
            if (seen.TryGetValue(key.Value, out var earlier))
            {
                Assert.Equal((earlier.Site, earlier.Area), (site, area));
                Assert.Equal(earlier.Parts, parts);
            }
            else
            {
                seen.Add(key.Value, (site, area, parts));
            }
        }

        Assert.True(seen.Count > 5_000, "The run drew too few distinct inputs to prove anything.");
    }

    [Fact]
    public void Site_work_with_no_site_is_a_bug()
    {
        var none = new SiteContext();

        Assert.Throws<InvalidOperationException>(() => SiteCacheKey.For(none, "page-data", "v1"));
        Assert.Throws<InvalidOperationException>(() => SiteCacheKey.Prefix(none));
        Assert.Throws<InvalidOperationException>(() => SiteCacheKey.Prefix(none, "page-data"));
        Assert.Throws<ArgumentNullException>(() => SiteCacheKey.For(null!, "page-data"));
    }

    // Only the site context a resolver entered names a key's site; a stand-in naming another site - or none at all, the
    // empty id - is refused for a key and for a prefix alike (tenancy.md rule 2).
    [Fact]
    public void A_site_context_no_resolver_entered_is_refused()
    {
        foreach (var forged in new[] { new Pinned(SiteB), new Pinned(new SiteId(Guid.Empty)) })
        {
            Assert.Throws<InvalidOperationException>(() => SiteCacheKey.For(forged, "page-data", "v1"));
            Assert.Throws<InvalidOperationException>(() => SiteCacheKey.Prefix(forged));
            Assert.Throws<InvalidOperationException>(() => SiteCacheKey.Prefix(forged, "page-data"));
        }
    }

    // Clearing one area reaches its keys and no other area's, even one whose name it begins.
    [Fact]
    public void An_area_prefix_covers_that_area_alone()
    {
        var site = Working(SiteA);
        var page = SiteCacheKey.Prefix(site, "page");

        Assert.Equal(SiteCacheKey.Prefix(site) + "page:", page);
        Assert.StartsWith(page, SiteCacheKey.For(site, "page", "x").Value, StringComparison.Ordinal);
        Assert.False(SiteCacheKey.For(site, "page-data", "x").Value.StartsWith(page, StringComparison.Ordinal));
        Assert.False(SiteCacheKey.For(site, "pages", "x").Value.StartsWith(page, StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => SiteCacheKey.Prefix(site, "Page"));
    }

    // A platform key - the resolution caches, address limits - is never inside any site's key space, even when it is
    // keyed by a slug or host spelled exactly like a site's id (PSEC-007's cache-key tricks).
    [Fact]
    public void A_platform_key_never_lands_in_a_sites_key_space()
    {
        var idShaped = SiteA.Value.ToString("D");
        var keys = new[]
        {
            PlatformCacheKey.For("site-slug", idShaped),
            PlatformCacheKey.For("site-host", idShaped + ".pantrypress.app"),
            PlatformCacheKey.For("site-slug", SiteCacheKey.Prefix(Working(SiteA)) + "page-data"),
            PlatformCacheKey.For("rate-ip"),
        };

        Assert.Equal("platform:site-slug:" + idShaped, keys[0].Value);
        Assert.Equal(keys[0].Value, keys[0].ToString());
        Assert.All(keys, key =>
        {
            Assert.StartsWith("platform:", key.Value, StringComparison.Ordinal);
            Assert.DoesNotMatch("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}:", key.Value);
            Assert.False(key.Value.StartsWith(SiteCacheKey.Prefix(Working(SiteA)), StringComparison.Ordinal));
        });
        Assert.Equal("platform:site-slug:" + SiteA.Value.ToString("D") + "%3Apage-data", keys[2].Value);
        Assert.Throws<ArgumentException>(() => PlatformCacheKey.For("Site-Slug", "x"));
        Assert.Throws<ArgumentException>(() => PlatformCacheKey.For("site-slug", ""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Page")]
    [InlineData("1page")]
    [InlineData("-page")]
    [InlineData("page:data")]
    [InlineData("page data")]
    [InlineData("page_data")]
    [InlineData("páge")]
    [InlineData("page%3A")]
    [InlineData("a12345678901234567890123456789012345678901")]
    public void An_area_outside_its_shape_is_refused(string area) =>
        Assert.Throws<ArgumentException>(() => SiteCacheKey.For(Working(SiteA), area));

    [Fact]
    public void Empty_parts_and_overlong_keys_are_refused()
    {
        var site = Working(SiteA);

        Assert.Throws<ArgumentException>(() => SiteCacheKey.For(site, null!));
        Assert.Throws<ArgumentException>(() => SiteCacheKey.For(site, "page-data", ""));
        Assert.Throws<ArgumentException>(() => SiteCacheKey.For(site, "page-data", "v1", null!));

        var room = SiteCacheKey.MaxBytes - SiteCacheKey.For(site, "page-data", "x").Value.Length + 1;
        Assert.Equal(SiteCacheKey.MaxBytes, SiteCacheKey.For(site, "page-data", new string('x', room)).Value.Length);
        Assert.Throws<ArgumentException>(() => SiteCacheKey.For(site, "page-data", new string('x', room + 1)));

        // The limit counts what the cache stores: the escaped key, in UTF-8.
        Assert.Throws<ArgumentException>(() => SiteCacheKey.For(site, "page-data", new string(':', room / 3 + 1)));
        Assert.Throws<ArgumentException>(() => SiteCacheKey.For(site, "page-data", new string('é', room / 2 + 1)));
        Assert.Throws<ArgumentException>(() => PlatformCacheKey.For("rate-ip", new string('x', SiteCacheKey.MaxBytes)));
    }

    // A lone surrogate would reach the cache as U+FFFD, where "a\uD800" and "a\uDC00" would be one key.
    [Fact]
    public void Malformed_text_is_refused_and_whole_characters_are_kept()
    {
        var site = Working(SiteA);

        Assert.Throws<ArgumentException>(() => SiteCacheKey.For(site, "search", "a\uD800"));
        Assert.Throws<ArgumentException>(() => SiteCacheKey.For(site, "search", "a\uDC00"));
        Assert.Throws<ArgumentException>(() => PlatformCacheKey.For("site-host", "\uDC00x"));
        Assert.NotEqual(SiteCacheKey.For(site, "search", "a😀").Value, SiteCacheKey.For(site, "search", "a�").Value);
    }

    [Fact]
    public void Keys_compare_ordinally_and_a_default_key_has_no_value()
    {
        Assert.Equal(SiteCacheKey.For(Working(SiteA), "page-data", "v1"), SiteCacheKey.For(Working(SiteA), "page-data", "v1"));
        Assert.NotEqual(SiteCacheKey.For(Working(SiteA), "page-data", "Pie"), SiteCacheKey.For(Working(SiteA), "page-data", "pie"));
        Assert.Null(default(SiteCacheKey).Value);
    }

    private sealed class Pinned(SiteId site) : ISiteContext
    {
        public SiteId? Current => site;

        public SiteId Required => site;
    }

    private static SiteContext Working(SiteId site)
    {
        var context = new SiteContext();
        context.Enter(site);
        return context;
    }

    private static void AssertDecodes(SiteCacheKey key, SiteId site, string area, string[] parts)
    {
        var decoded = Decode(key.Value);
        Assert.Equal(site, decoded.Site);
        Assert.Equal(area, decoded.Area);
        Assert.Equal(parts, decoded.Parts);
    }

    // The inverse of the key's construction, written independently of it: split on the separator, then undo the escapes
    // left to right.
    private static (SiteId Site, string Area, string[] Parts) Decode(string key)
    {
        var segments = key.Split(':');
        var parts = segments[2..].Select(Unescape).ToArray();
        return (new SiteId(Guid.ParseExact(segments[0], "D")), segments[1], parts);
    }

    private static string Unescape(string part)
    {
        var text = new StringBuilder();
        for (var i = 0; i < part.Length; i++)
        {
            if (part[i] != '%')
            {
                text.Append(part[i]);
                continue;
            }

            text.Append(part.Substring(i, 3) switch
            {
                "%25" => '%',
                "%3A" => ':',
                var other => throw new FormatException($"Not an escape the key makes: {other}"),
            });
            i += 2;
        }

        return text.ToString();
    }
}
