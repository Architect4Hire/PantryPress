using Microsoft.AspNetCore.Mvc;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Site.Hosting;

namespace PantryPress.Site.Controllers;

/// <summary>
/// The site a reader's Host names (SITE-008, SEO-007; PP2.13): its name, tagline, language, and canonical host, and nothing
/// private. The site comes from host resolution alone, never the request (tenancy.md rules 5 and 7), and its status is
/// answered first by <see cref="SiteStatusFilter"/>. Not a call the SSR server makes per page: a page makes one page-data
/// fetch (rendering.md), so PP7.13's page data carries this context, and its 301.
/// </summary>
[ApiController]
[Route(PublicSurface.V1Route)]
public sealed class SiteContextController(IPublicSitesFacade sites, IPublicSiteContext site) : ControllerBase
{
    /// <summary>
    /// How long a reader's browser or the edge may keep the answer. Short, because a site that closes should stop showing
    /// its name soon even before the edge is purged on a status change (PP9.5); a closed site's answer is never kept.
    /// </summary>
    internal const string CacheControl = "public, max-age=60";

    /// <summary>
    /// The site's public context. When the request's host is not the canonical one, a page answers 301 to it; this answer
    /// says where, and does not redirect itself.
    /// </summary>
    [HttpGet("site")]
    [ProducesResponseType<PublicSiteDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError,
        "application/problem+json",
        Description = "unexpected-error: a fault on our side.")]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status404NotFound,
        "application/problem+json",
        Description = "not-found: the host names no site, the same answer an unknown path gets.")]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status410Gone,
        "application/problem+json",
        Description = "site-gone: the site was deleted.")]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status503ServiceUnavailable,
        "application/problem+json",
        Description = "site-coming-soon: the site is being set up (Retry-After: 30). site-unavailable: the site is closed "
            + "for now, for whatever reason (Retry-After: 3600).")]
    public IActionResult Get()
    {
        var result = sites.GetContext(site.Site);
        if (!result.IsSuccess)
        {
            return SiteProblems.FromError(result.Error);
        }

        Response.Headers.CacheControl = CacheControl;
        return Ok(result.Value);
    }
}
