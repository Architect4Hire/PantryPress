using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.ServiceDefaults.EdgeHardening;

namespace PantryPress.Site.Hosting;

/// <summary>
/// The site this public request is for, resolved from its Host header before any endpoint runs (ARCH-003, PSEC-004;
/// PP2.12). Every public endpoint reads its site here and nowhere else: never from a route, body, query, cookie, or other
/// header (tenancy.md rule 7).
/// </summary>
public interface IPublicSiteContext
{
    /// <summary>The site; an endpoint is never reached without one.</summary>
    PublicSite Site { get; }
}

/// <summary>
/// Holds what resolution answered. It agrees with the request's <see cref="ISiteContext"/> or it holds nothing: the
/// resolver entered that site, so a public site that names another is a bug, and only <see cref="PublicSiteHosts"/> sets
/// it (<c>DomainBoundaryTests</c>).
/// </summary>
internal sealed class PublicSiteContext(ISiteContext siteContext) : IPublicSiteContext
{
    private PublicSite? _site;

    public PublicSite Site => _site
        ?? throw new InvalidOperationException("This request reached an endpoint without a site; host resolution did not run.");

    public void Enter(PublicSite site)
    {
        if (_site is not null)
        {
            throw new InvalidOperationException("This request's site was already resolved.");
        }

        if (siteContext.Current != site.SiteId)
        {
            throw new InvalidOperationException("A public site must be the one host resolution entered.");
        }

        _site = site;
    }
}

/// <summary>
/// Host resolution for every request this host answers (PP2.12). A Host that names no active site - unknown, pending, an
/// IP address, a malformed name or port - is answered the platform's neutral 404, the same document an unknown path on a
/// real site gets, so a reader cannot tell a real host from a guess (ARCH-003). Health probes are exempt, as they are from
/// every other edge step: a probe addresses a container by whatever name it likes. They are mapped only in Development
/// today and short-circuit before this runs; a deployed probe is PP19's.
/// </summary>
internal static class PublicSiteHosts
{
    public static IServiceCollection AddPublicSiteContext(this IServiceCollection services)
    {
        services.AddScoped<PublicSiteContext>();
        services.AddScoped<IPublicSiteContext>(sp => sp.GetRequiredService<PublicSiteContext>());
        return services;
    }

    /// <summary>
    /// Resolves the site, or ends the request with the problem 404 an unmatched route gets. Runs after the
    /// forwarding-header strip, so only the Host the connection carried is read; two Host headers name no host at all.
    /// The request target and HTTP/2's <c>:authority</c> are Kestrel's to reconcile with Host before this runs.
    /// </summary>
    public static IApplicationBuilder UseSiteHostResolution(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (IsProbe(context.Request.Path))
            {
                await next(context);
                return;
            }

            var hosts = context.Request.Headers.Host;
            var site = hosts.Count == 1
                ? await context.RequestServices.GetRequiredService<IPublicSiteResolution>()
                    .ResolveAsync(hosts[0], context.RequestAborted)
                : null;
            if (site is null)
            {
                await SiteProblems.WriteAsync(context, StatusCodes.Status404NotFound);
                return;
            }

            context.RequestServices.GetRequiredService<PublicSiteContext>().Enter(site);
            await next(context);
        });

    private static bool IsProbe(PathString path) =>
        EdgeHttps.Exempt.Any(exempt => path.StartsWithSegments(exempt, StringComparison.OrdinalIgnoreCase));
}
