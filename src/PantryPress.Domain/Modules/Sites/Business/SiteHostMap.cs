using System.Globalization;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Business;

/// <summary>What an active host names: its site, the site's status, and the site's canonical host.</summary>
/// <param name="CanonicalHost">The site's canonical host, or the host itself when a deleted site has none left.</param>
public sealed record SiteHostMatch(SiteId SiteId, SiteStatus Status, bool IsCanonical, string CanonicalHost);

/// <summary>
/// The public tier's host map in the platform cache (PP2.12): one entry per stored-form host, under
/// <c>platform:site-host:{host}</c>, holding what the host names or that it names nothing. Keyed by host rather than by
/// site, because no site is known yet - the written exception to tenancy.md rule 8 - and refreshed on every write to a
/// site's hosts and every change of its status, which is what keeps a stale entry from sending one site's readers to
/// another (tenancy.md, "Tenant root").
/// </summary>
public static class SiteHostMap
{
    /// <summary>The cache area of the map.</summary>
    public const string Area = "site-host";

    /// <summary>
    /// How long a host that names a site stays cached. Writes refresh the map, so this only bounds an entry a refresh
    /// failed to reach - the cache down when the write committed.
    /// </summary>
    public static readonly TimeSpan FoundLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a host that names nothing stays cached. Short, because a site created while the cache could not be told
    /// must not stay "not found" for long (tenant-provisioning G-02); long enough that a flood of one unknown host is not
    /// a flood of queries.
    /// </summary>
    public static readonly TimeSpan NotFoundLifetime = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long a lookup may take from reading the map to filling it. One that took longer - a database failing over, a
    /// stalled process - answers its request but caches nothing, since its answer may already be older than a refresh the
    /// cache has since forgotten. Far shorter than <see cref="NotFoundLifetime"/>.
    /// </summary>
    public static readonly TimeSpan MaxFillDelay = TimeSpan.FromSeconds(5);

    private const string NotFound = "-";
    private const string FormatVersion = "1";
    private const char Separator = '|';

    /// <summary>The entry for <paramref name="match"/>, or for no site when it is null.</summary>
    public static string Write(SiteHostMatch? match) => match is null
        ? NotFound
        : string.Join(
            Separator,
            FormatVersion,
            match.SiteId.Value.ToString("D"),
            ((int)match.Status).ToString(CultureInfo.InvariantCulture),
            match.IsCanonical ? "1" : "0",
            match.CanonicalHost);

    /// <summary>
    /// Reads an entry. False when it is not one this version wrote - from an older format, or damaged - which the caller
    /// treats as a miss, so the database answers and the entry is written again. True with a null match for a host that
    /// names nothing.
    /// </summary>
    public static bool TryRead(string entry, out SiteHostMatch? match)
    {
        match = null;
        if (entry == NotFound)
        {
            return true;
        }

        var fields = entry.Split(Separator);
        if (fields is not [FormatVersion, var site, var status, var canonical, var canonicalHost]
            || !Guid.TryParseExact(site, "D", out var siteId)
            || siteId == Guid.Empty
            || !int.TryParse(status, NumberStyles.None, CultureInfo.InvariantCulture, out var statusValue)
            || !Enum.IsDefined((SiteStatus)statusValue)
            || canonical is not ("0" or "1")
            || !SiteHostNames.TryNormalize(canonicalHost, out var stored)
            || stored != canonicalHost)
        {
            return false;
        }

        match = new SiteHostMatch(new SiteId(siteId), (SiteStatus)statusValue, canonical == "1", canonicalHost);
        return true;
    }
}
