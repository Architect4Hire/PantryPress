using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>
/// Inviting people to a site by email and joining one through an invitation (SITE-010; PP2.11). An invitation is
/// site-owned data. Inviting works on the site its route's Owner policy entered; accepting works on the site sealed inside
/// the invitation's link, which the caller is not yet a member of (tenancy.md rule 4).
/// </summary>
public interface ISiteInvitationsFacade
{
    /// <summary>
    /// Invites <see cref="InviteMemberRequest.Email"/> to the site the request's Owner policy admitted the account to, by
    /// its <paramref name="siteSlug"/>, and emails them a link that works once, until
    /// <see cref="SiteInvitationDetail.ExpiresAt"/>. The answer is the same whether or not the address has an account.
    /// An open invitation to the same address is replaced: its link stops working. <see cref="SiteErrors.TooManyInvitations"/>
    /// when the site already has as many open invitations as it may.
    /// </summary>
    Task<Result<SiteInvitationDetail>> InviteAsync(
        AccountId account,
        string siteSlug,
        InviteMemberRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Makes <paramref name="account"/> a member of the invitation's site with the invitation's role, and enters that site
    /// as the request's. The account's address must be the invited one, confirmed: another address is
    /// <see cref="SiteErrors.InvitationForOtherAddress"/>, the same one unconfirmed <c>AccountErrors.EmailNotVerified</c>. A
    /// link that is not one of ours, has expired, was used or replaced, or is for a deleted site is
    /// <see cref="SiteErrors.InvitationInvalid"/>, all alike; an account that is already a member there, suspended or not,
    /// is <see cref="SiteErrors.AlreadyMember"/> and nothing changes.
    /// </summary>
    Task<Result<SiteSummary>> AcceptAsync(AccountId account, AcceptInvitationRequest request, CancellationToken cancellationToken);
}
