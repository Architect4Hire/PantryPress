using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>The caller's standing on one site: which site the route's slug named, and the role held there.</summary>
public sealed record SiteMembership(SiteId SiteId, SiteRole Role);

/// <summary>
/// Site membership (SITE-010; PP2.3), read to authorize admin site routes and to resolve their site (PP2.4). Scoped to one
/// request: the lookup is made once per account and slug however many policies ask (auth.md), and never remembered past
/// the request.
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
    /// The account's membership when its role covers <paramref name="leastRole"/>, and that site becomes the request's
    /// <see cref="ISiteContext"/> (tenancy.md rule 4): the one way an admin request gets a site, so nothing a client sends
    /// can name one. <see cref="SiteErrors.SiteNotFound"/> whenever <see cref="FindAsync"/> would answer null;
    /// <see cref="SiteErrors.SiteRoleRequired"/> for a member whose role is too low, who already knows the site is there.
    /// A refusal leaves the context as it was. Authorizing a second site in one request is a bug, and throws: a question
    /// about another site the caller holds asks <see cref="FindAsync"/>, which enters nothing.
    /// </summary>
    Task<Result<SiteMembership>> AuthorizeAsync(
        AccountId account,
        string siteSlug,
        SiteRole leastRole,
        CancellationToken cancellationToken) =>
        AuthorizeAsync(account, siteSlug, [leastRole], cancellationToken);

    /// <summary>
    /// As the single-role form, for a route whose stacked site policies each name a least role: one decision for all of
    /// them, so the site is entered only when the caller's role covers every one, and a 403 on any leaves no site.
    /// </summary>
    Task<Result<SiteMembership>> AuthorizeAsync(
        AccountId account,
        string siteSlug,
        IReadOnlyCollection<SiteRole> leastRoles,
        CancellationToken cancellationToken);
}
