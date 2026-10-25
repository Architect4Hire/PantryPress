using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>The caller's standing on one site: which site the route's slug named, and the role held there.</summary>
public sealed record SiteMembership(SiteId SiteId, SiteRole Role);

/// <summary>
/// Site membership (SITE-010; PP2.3), read to authorize admin site routes. Scoped to one request: the lookup is made once
/// per account and slug however many policies ask (auth.md), and never remembered past the request.
/// </summary>
public interface ISiteMembershipFacade
{
    /// <summary>
    /// The account's active membership on the site with this slug, or null. Null alike for no such site, a deleted
    /// site, no membership, and a suspended one, so nothing about a site the caller cannot reach is revealed (tenancy.md,
    /// "Tenant root"). The slug is compared case-insensitively, and anything outside its shape finds nothing.
    /// </summary>
    Task<SiteMembership?> FindAsync(AccountId account, string siteSlug, CancellationToken cancellationToken);

    /// <summary>
    /// The account's membership when its role covers <paramref name="leastRole"/>. <see cref="SiteErrors.SiteNotFound"/>
    /// whenever <see cref="FindAsync"/> would answer null; <see cref="SiteErrors.SiteRoleRequired"/> for a member whose
    /// role is too low, who already knows the site is there.
    /// </summary>
    Task<Result<SiteMembership>> AuthorizeAsync(
        AccountId account,
        string siteSlug,
        SiteRole leastRole,
        CancellationToken cancellationToken);
}
