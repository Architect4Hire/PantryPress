using Microsoft.AspNetCore.Mvc.Filters;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Site.Hosting;

/// <summary>
/// The status answers (SITE-008, SEO-007; PP2.13), ahead of every public controller's own work: a site that is not open to
/// readers - coming soon, unavailable, or gone - is answered that, before model binding and before anything of the
/// site's is read (tenancy.md rule 5). Registered globally, so an endpoint cannot forget it. Unknown paths are untouched:
/// they get the neutral 404 whatever the site's status, as an unknown host does.
/// </summary>
internal sealed class SiteStatusFilter(IPublicSitesFacade sites, IPublicSiteContext site) : IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (sites.CheckStatus(site.Site).Error is { } refusal)
        {
            context.Result = SiteProblems.FromError(refusal);
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
