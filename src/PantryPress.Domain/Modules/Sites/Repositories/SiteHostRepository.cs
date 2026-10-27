using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;

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

    public void Add(SiteHost host) => db.Set<SiteHost>().Add(host);
}
