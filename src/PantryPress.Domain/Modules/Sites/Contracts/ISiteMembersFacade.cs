using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>
/// A site's members, as its Owner manages them (SITE-010; PP2.11b): who they are, their roles, and removing them. Every
/// call works on the site its route's Owner policy entered (tenancy.md rule 4), and names a member by their account's id
/// within that site only: an id that is not a member here - unknown, malformed, or another site's member - is one answer,
/// <see cref="SiteErrors.MemberNotFound"/>. Every site keeps an active Owner: a change that would leave none is
/// <see cref="SiteErrors.LastOwner"/>, whether the rule sees it first or a change racing it does.
/// </summary>
public interface ISiteMembersFacade
{
    /// <summary>The site's members, active and suspended, in the order they joined, with each one's address.</summary>
    Task<Result<SiteMemberPage>> ListAsync(
        AccountId account,
        string siteSlug,
        ListSiteMembersQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gives the member <see cref="ChangeMemberRoleRequest.Role"/>. Their standing - active or suspended - is kept. Giving
    /// the role they already have succeeds and changes nothing. <see cref="SiteErrors.MemberChanged"/> when someone else
    /// changed the same member at the same moment. The member's next request works with the new role.
    /// </summary>
    Task<Result> ChangeRoleAsync(
        AccountId account,
        string siteSlug,
        string memberId,
        ChangeMemberRoleRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes the member from the site; their next request reaches it no more. An Owner removing themselves leaves the
    /// site. The audit log keeps the history; an invitation can bring them back.
    /// </summary>
    Task<Result> RemoveAsync(AccountId account, string siteSlug, string memberId, CancellationToken cancellationToken);
}
