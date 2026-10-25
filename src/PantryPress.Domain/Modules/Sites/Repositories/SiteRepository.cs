using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Repositories;

/// <summary>
/// The tenant root (PP2.2). Nothing filters this table, so every read here is keyed and hands back no more than its
/// caller needs (tenancy.md, "Tenant root").
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
}
