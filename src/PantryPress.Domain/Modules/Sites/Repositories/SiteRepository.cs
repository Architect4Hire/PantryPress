using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Repositories;

/// <summary>One of an account's sites, with the account's role there, as the list reads it.</summary>
internal sealed record MemberSite(SiteId Id, string Slug, string Name, SiteStatus Status, SiteRole Role);

/// <summary>
/// The tenant root (PP2.2). Nothing filters this table, so every read here is keyed and hands back no more than its
/// caller needs (tenancy.md, "Tenant root"): by a slug only whether it is there, by a site already resolved its row, and
/// a list only through the caller's own memberships.
/// </summary>
internal sealed class SiteRepository(PantryPressDbContext db)
{
    /// <summary>The id of the site with this stored-form slug, unless it is deleted. Only the id: never the row.</summary>
    public async Task<SiteId?> FindIdBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var ids = await db.Set<Site>()
            .AsNoTracking()
            .Where(s => s.Slug == slug && s.Status != SiteStatus.Deleted)
            .Select(s => s.Id)
            .Take(1)
            .ToListAsync(cancellationToken);
        return ids is [var id] ? id : null;
    }

    /// <summary>
    /// Whether any site holds this stored-form slug, a deleted one included, since a deleted site's slug is held from
    /// reissue. Existence only (SITE-002).
    /// </summary>
    public Task<bool> IsSlugHeldAsync(string slug, CancellationToken cancellationToken) =>
        db.Set<Site>().AnyAsync(s => s.Slug == slug, cancellationToken);

    /// <summary>The row of a site resolution has already authorized, unless it is deleted.</summary>
    public Task<Site?> FindAsync(SiteId site, CancellationToken cancellationToken) =>
        db.Set<Site>()
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == site && s.Status != SiteStatus.Deleted, cancellationToken);

    /// <summary>
    /// Up to <paramref name="take"/> of the account's sites, through its active memberships, after
    /// <paramref name="afterSlug"/> in slug order. Deleted sites are left out. The slug column is binary-collated, so
    /// the order and the comparison are byte order, the order the cursor assumes.
    /// </summary>
    public async Task<IReadOnlyList<MemberSite>> ListForMemberAsync(
        AccountId account,
        string? afterSlug,
        int take,
        CancellationToken cancellationToken)
    {
        var sites = db.Set<Site>().AsNoTracking().Where(s => s.Status != SiteStatus.Deleted);
        if (afterSlug is not null)
        {
            sites = sites.Where(s => s.Slug.CompareTo(afterSlug) > 0);
        }

        return await db.Set<SiteMember>()
            .AsNoTracking()
            .Where(m => m.AccountId == account && m.Status == SiteMemberStatus.Active)
            .Join(sites, m => m.SiteId, s => s.Id, (m, s) => new { Site = s, m.Role })
            .OrderBy(x => x.Site.Slug)
            .Take(take)
            .Select(x => new MemberSite(x.Site.Id, x.Site.Slug, x.Site.Name, x.Site.Status, x.Role))
            .ToListAsync(cancellationToken);
    }

    public void Add(Site site) => db.Set<Site>().Add(site);
}
