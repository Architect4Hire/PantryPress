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

    /// <summary>
    /// How many sites the account owns, through any Owner membership, active or suspended, on a site of any status, a
    /// deleted one included: a deleted site still holds its slug. Keyed by the account alone, on its own index.
    /// </summary>
    public Task<int> CountOwnedAsync(AccountId account, CancellationToken cancellationToken) =>
        db.Set<SiteMember>().CountAsync(m => m.AccountId == account && m.Role == SiteRole.Owner, cancellationToken);

    /// <summary>
    /// Whether the account has any membership on the site, active or suspended (PP2.11): an invitation never adds a second
    /// one, and never lifts a suspension.
    /// </summary>
    public Task<bool> IsMemberAsync(SiteId site, AccountId account, CancellationToken cancellationToken) =>
        db.Set<SiteMember>().AnyAsync(m => m.SiteId == site && m.AccountId == account, cancellationToken);

    public void Add(SiteMember member) => db.Set<SiteMember>().Add(member);
}
