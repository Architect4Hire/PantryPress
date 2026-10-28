using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Repositories;

/// <summary>
/// A site's hosts (PP2.9). Nothing filters this table, so every read is keyed by a stored-form host or by sites already
/// resolved (tenancy.md, "Tenant root").
/// </summary>
internal sealed class SiteHostRepository(PantryPressDbContext db)
{
    /// <summary>Whether any site holds this stored-form host, in any kind or status. Existence only.</summary>
    public Task<bool> IsHostHeldAsync(string host, CancellationToken cancellationToken) =>
        db.Set<SiteHost>().AnyAsync(h => h.Host == host, cancellationToken);

    /// <summary>The canonical host of each of <paramref name="sites"/>, which resolution has already authorized.</summary>
    public async Task<IReadOnlyDictionary<SiteId, string>> FindCanonicalAsync(
        IReadOnlyCollection<SiteId> sites,
        CancellationToken cancellationToken)
    {
        if (sites.Count == 0)
        {
            return new Dictionary<SiteId, string>();
        }

        var ids = sites.ToList();
        var hosts = await db.Set<SiteHost>()
            .AsNoTracking()
            .Where(h => ids.Contains(h.SiteId) && h.IsCanonical)
            .Select(h => new { h.SiteId, h.Host })
            .ToListAsync(cancellationToken);
        return hosts.ToDictionary(h => h.SiteId, h => h.Host);
    }

    /// <summary>
    /// What this stored-form host names when it is active (PP2.12): its site, the site's status, and the site's canonical
    /// host; or null. A pending host names nothing, exactly like one no site holds. One query, keyed by the host.
    /// </summary>
    public async Task<SiteHostMatch?> FindActiveAsync(string host, CancellationToken cancellationToken)
    {
        var found = await (
                from h in db.Set<SiteHost>().AsNoTracking()
                where h.Host == host && h.Status == SiteHostStatus.Active
                join s in db.Set<Site>().AsNoTracking() on h.SiteId equals s.Id
                select new
                {
                    h.SiteId,
                    s.Status,
                    h.IsCanonical,
                    Canonical = db.Set<SiteHost>()
                        .Where(c => c.SiteId == h.SiteId && c.IsCanonical)
                        .Select(c => c.Host)
                        .FirstOrDefault(),
                })
            .Take(1)
            .ToListAsync(cancellationToken);
        return found is [var row]
            ? new SiteHostMatch(row.SiteId, row.Status, row.IsCanonical, row.Canonical ?? host)
            : null;
    }

    /// <summary>Every host of a site resolution has already authorized, in any kind or status.</summary>
    public async Task<IReadOnlyList<string>> ListHostsAsync(SiteId site, CancellationToken cancellationToken) =>
        await db.Set<SiteHost>()
            .AsNoTracking()
            .Where(h => h.SiteId == site)
            .Select(h => h.Host)
            .ToListAsync(cancellationToken);

    public void Add(SiteHost host) => db.Set<SiteHost>().Add(host);
}
