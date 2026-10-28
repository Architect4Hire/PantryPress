using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Facades;

/// <summary>
/// The public tier's answers for a site host resolution already found (PP2.13). Nothing here reads: the site, its status,
/// and its public identity came from the host map with the resolution, so a warm request makes no query, and nothing here
/// enters a site. It answers only for the site the request resolved: a public site that names another is a bug.
/// </summary>
internal sealed class PublicSitesFacade(ISiteContext siteContext) : IPublicSitesFacade
{
    public Result CheckStatus(PublicSite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        if (siteContext.Current != site.SiteId)
        {
            throw new InvalidOperationException("A public site must be the one host resolution entered.");
        }

        return PublicSiteStatus.RefusalFor(site.Status) is { } refusal ? refusal : Result.Success;
    }

    public Result<PublicSiteDetail> GetContext(PublicSite site)
    {
        if (CheckStatus(site).Error is { } refusal)
        {
            return refusal;
        }

        var identity = site.Identity;
        return new PublicSiteDetail(identity.Name, identity.Tagline, identity.Locale, site.CanonicalHost, site.IsCanonical);
    }
}
