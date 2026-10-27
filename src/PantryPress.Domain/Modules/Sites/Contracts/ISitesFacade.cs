using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>
/// Creating a site, and what its members read of it (SITE-002, SITE-003; PP2.10). Account-level: create, list, and
/// availability work on no site; reading one works on the site its route's policy entered (tenancy.md rule 4).
/// </summary>
public interface ISitesFacade
{
    /// <summary>
    /// Creates a <see cref="SiteStatus.Live"/> site, its canonical subdomain, and the account as its active Owner, in one
    /// transaction, and enters that site as the request's - even when the save then loses the address to another
    /// request, so nothing after a refusal may work on the request's site. <see cref="SiteErrors.SlugUnavailable"/> for a
    /// slug that is reserved, held by any site - a deleted one included - or still another site's address, all alike; a
    /// refused shape is a <c>slug</c> field error. An account whose email address is not confirmed may own only
    /// <c>Sites:UnconfirmedAccountSiteLimit</c> sites, deleted ones included; past that it gets
    /// <c>AccountErrors.EmailNotVerified</c>, before anything about the address is checked. Plan limits are not checked
    /// until PP14 brings the entitlements facade.
    /// </summary>
    Task<Result<SiteDetail>> CreateAsync(AccountId account, CreateSiteRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The sites the account is an active member of, deleted ones left out, ordered by slug. Never another account's,
    /// and never a site through a suspended membership.
    /// </summary>
    Task<Result<SitePage>> ListAsync(AccountId account, ListSitesQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// The site the request's site policy admitted the account to, by its <paramref name="siteSlug"/>: call it only behind
    /// a site policy, which has already answered <see cref="SiteErrors.SiteNotFound"/> for anyone it would not tell.
    /// </summary>
    Task<Result<SiteDetail>> GetAsync(AccountId account, string siteSlug, CancellationToken cancellationToken);

    /// <summary>Whether a site could take <paramref name="slug"/> now. A refused shape is a <c>slug</c> field error.</summary>
    Task<Result<SlugAvailability>> CheckAvailabilityAsync(SlugAvailabilityQuery query, CancellationToken cancellationToken);
}
