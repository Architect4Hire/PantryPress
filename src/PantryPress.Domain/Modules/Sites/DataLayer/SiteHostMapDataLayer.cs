using Microsoft.Extensions.Logging;
using PantryPress.Domain.Managers.Caching;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Repositories;

namespace PantryPress.Domain.Modules.Sites.DataLayer;

/// <summary>
/// The host map (PP2.12): what a host names, from the platform cache when it holds the answer and from <c>SiteHosts</c>
/// when it does not, and the refresh every write to a site's hosts or status owes it (tenancy.md, "Tenant root").
/// </summary>
/// <remarks>
/// The cache is an optimization, never the authority. When it cannot be reached a lookup reads the database and caches
/// nothing, and a refresh is logged and skipped: the entry it missed lives at most <see cref="SiteHostMap.FoundLifetime"/>
/// or <see cref="SiteHostMap.NotFoundLifetime"/>. A lookup that raced a write is not cached at all, because the write
/// moved the entry's version (<see cref="IPlatformCache"/>). Nor is one that took longer than
/// <see cref="SiteHostMap.MaxFillDelay"/> from its read to its fill: the version is the guard, and this is the second one,
/// for a version the cache lost before the fill arrived - evicted under memory pressure, say.
/// </remarks>
internal sealed partial class SiteHostMapDataLayer(
    SiteHostRepository hosts,
    IPlatformCache cache,
    IClock clock,
    ILogger<SiteHostMapDataLayer> logger)
{
    /// <summary>What the stored-form <paramref name="host"/> names, or null.</summary>
    public async Task<SiteHostMatch?> FindAsync(string host, CancellationToken cancellationToken)
    {
        var key = KeyFor(host);
        var asked = clock.UtcNow;
        CacheRead seen;
        try
        {
            seen = await cache.ReadAsync(key);
        }
        catch (CacheUnavailableException failure)
        {
            LogCacheUnavailable(failure);
            return await hosts.FindActiveAsync(host, cancellationToken);
        }

        if (seen.Value is { } entry && SiteHostMap.TryRead(entry, out var cached))
        {
            return cached;
        }

        var found = await hosts.FindActiveAsync(host, cancellationToken);
        if (clock.UtcNow - asked > SiteHostMap.MaxFillDelay)
        {
            return found;
        }

        try
        {
            await cache.WriteAsync(
                key,
                seen,
                SiteHostMap.Write(found),
                found is null ? SiteHostMap.NotFoundLifetime : SiteHostMap.FoundLifetime);
        }
        catch (CacheUnavailableException failure)
        {
            LogCacheUnavailable(failure);
        }

        return found;
    }

    /// <summary>
    /// Drops what the map holds for each of <paramref name="hostNames"/>, after the write that changed them committed, so
    /// the next request reads the database. Every write to a site's hosts names the hosts it added, removed, or changed.
    /// </summary>
    public async Task RefreshAsync(IEnumerable<string> hostNames)
    {
        foreach (var host in hostNames.Distinct(StringComparer.Ordinal))
        {
            try
            {
                await cache.InvalidateAsync(KeyFor(host));
            }
            catch (CacheUnavailableException failure)
            {
                LogRefreshSkipped(failure);
                return;
            }
        }
    }

    /// <summary>
    /// Drops what the map holds for every host of <paramref name="site"/>, which resolution has already authorized: what a
    /// change of the site's status owes, since every host's entry carries it.
    /// </summary>
    public async Task RefreshSiteAsync(SiteId site, CancellationToken cancellationToken) =>
        await RefreshAsync(await hosts.ListHostsAsync(site, cancellationToken));

    private static PlatformCacheKey KeyFor(string host) => PlatformCacheKey.For(SiteHostMap.Area, host);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The host map's cache could not be reached; the database answered.")]
    private partial void LogCacheUnavailable(Exception failure);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The host map's cache could not be reached to refresh it; its entries expire on their own.")]
    private partial void LogRefreshSkipped(Exception failure);
}
