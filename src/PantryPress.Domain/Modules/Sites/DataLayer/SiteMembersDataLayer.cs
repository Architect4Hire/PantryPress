using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.Repositories;

namespace PantryPress.Domain.Modules.Sites.DataLayer;

/// <summary>
/// Composes the membership repository for an Owner managing the site's members (ARCH-002; PP2.11b). Nothing filters the
/// membership table, so every read here is keyed by the site the request already entered, and by a member's account
/// where it names one (tenancy.md, "Tenant root").
/// </summary>
internal sealed class SiteMembersDataLayer(SiteMemberRepository members, SitesUnitOfWork unitOfWork)
{
    /// <summary>
    /// Up to <paramref name="limit"/> of the site's members after <paramref name="after"/>, and whether more follow.
    /// </summary>
    public async Task<(IReadOnlyList<SiteMember> Items, bool More)> ListAsync(
        SiteId site,
        SiteMemberPosition? after,
        int limit,
        CancellationToken cancellationToken)
    {
        // One more than the page, to know whether another follows without a count.
        var found = await members.ListPageAsync(site, after, limit + 1, cancellationToken);
        return ([.. found.Take(limit)], found.Count > limit);
    }

    /// <summary>The account's membership of the site, tracked, or null.</summary>
    public Task<SiteMember?> FindMemberAsync(SiteId site, AccountId account, CancellationToken cancellationToken) =>
        members.FindTrackedAsync(site, account, cancellationToken);

    public Task<int> CountActiveOwnersAsync(SiteId site, CancellationToken cancellationToken) =>
        members.CountActiveOwnersAsync(site, cancellationToken);

    /// <summary>Stages the member's new role.</summary>
    public static void StageRoleChange(SiteMember member, SiteRole role) => member.Role = role;

    /// <summary>Stages the member's removal.</summary>
    public void StageRemoval(SiteMember member) => members.Remove(member);

    /// <summary>Saves the staged role or removal; <paramref name="newRole"/> is null for a removal.</summary>
    public Task<MemberChangeOutcome> SaveMemberChangeAsync(
        SiteId site,
        AccountId member,
        SiteRole? newRole,
        CancellationToken cancellationToken) =>
        unitOfWork.SaveMemberChangeAsync(site, member, newRole, cancellationToken);
}
