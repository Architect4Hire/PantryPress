using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Repositories;

/// <summary>
/// Site memberships (PP2.3). Nothing filters this table, so every read is keyed by the site and the account together
/// (tenancy.md, "Tenant root").
/// </summary>
internal sealed class SiteMemberRepository(PantryPressDbContext db)
{
    /// <summary>The account's role on the site, if its membership there is active.</summary>
    public async Task<SiteRole?> FindActiveRoleAsync(SiteId site, AccountId account, CancellationToken cancellationToken)
    {
        var roles = await db.Set<SiteMember>()
            .AsNoTracking()
            .Where(m => m.SiteId == site && m.AccountId == account && m.Status == SiteMemberStatus.Active)
            .Select(m => m.Role)
            .ToListAsync(cancellationToken);
        return roles is [var role] ? role : null;
    }
}
