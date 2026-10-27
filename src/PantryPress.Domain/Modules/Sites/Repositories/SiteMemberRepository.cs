using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Business;
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

    /// <summary>
    /// Up to <paramref name="take"/> of the site's members, active and suspended, after <paramref name="after"/> in join
    /// order, then account order (PP2.11b). The keyset comparison is SQL Server's own uniqueidentifier order, the same
    /// order the list is sorted in, so a page after a cursor neither repeats nor skips.
    /// </summary>
    public Task<List<SiteMember>> ListPageAsync(
        SiteId site,
        SiteMemberPosition? after,
        int take,
        CancellationToken cancellationToken)
    {
        var members = after is { } position
            ? db.Set<SiteMember>().FromSql($"""
                SELECT * FROM [SiteMembers]
                WHERE [SiteId] = {site.Value}
                  AND ([CreatedAt] > {position.JoinedAt}
                    OR ([CreatedAt] = {position.JoinedAt} AND [AccountId] > {position.Account.Value}))
                """)
            : db.Set<SiteMember>().Where(m => m.SiteId == site);
        return members
            .AsNoTracking()
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.AccountId)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    /// <summary>The account's membership of the site, tracked so a change to it can be saved, or null (PP2.11b).</summary>
    public Task<SiteMember?> FindTrackedAsync(SiteId site, AccountId account, CancellationToken cancellationToken) =>
        db.Set<SiteMember>().SingleOrDefaultAsync(m => m.SiteId == site && m.AccountId == account, cancellationToken);

    /// <summary>How many active Owners the site has now (PP2.11b).</summary>
    public Task<int> CountActiveOwnersAsync(SiteId site, CancellationToken cancellationToken) =>
        db.Set<SiteMember>().CountAsync(
            m => m.SiteId == site && m.Role == SiteRole.Owner && m.Status == SiteMemberStatus.Active,
            cancellationToken);

    /// <summary>Stages a membership's removal, to be saved with whatever else the facade staged.</summary>
    public void Remove(SiteMember member) => db.Set<SiteMember>().Remove(member);
}
