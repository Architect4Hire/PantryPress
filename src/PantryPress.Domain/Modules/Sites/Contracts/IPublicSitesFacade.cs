using PantryPress.Domain.Managers.Results;

namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>
/// What the public tier answers for a site, given the site host resolution found (tenancy.md rule 5; PP2.13). Its status
/// decides first: a site that is not open to readers has one neutral answer per kind of closed (<see cref="SiteErrors"/>),
/// and a live site and a read-only one are answered alike, because a failed payment is the owner's business.
/// </summary>
public interface IPublicSitesFacade
{
    /// <summary>
    /// Success when the site is open to readers; otherwise the answer every public endpoint gives in place of its own -
    /// coming soon, unavailable, or gone - before it reads anything of the site's.
    /// </summary>
    Result CheckStatus(PublicSite site);

    /// <summary>The site's public context, or <see cref="CheckStatus"/>'s refusal.</summary>
    Result<PublicSiteDetail> GetContext(PublicSite site);
}
