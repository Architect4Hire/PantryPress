using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.DataLayer;

namespace PantryPress.Domain.Modules.Sites.Facades;

/// <summary>
/// The public tier's resolver (tenancy.md rule 5; PP2.12): a request's site from its Host header alone, through the host
/// map. It is one of the few places a site is entered, listed by this file in <c>DomainBoundaryTests</c>: the Host is the
/// only thing an anonymous request has that names a site, and a site is entered only when one of its active hosts is
/// exactly that host.
/// </summary>
internal sealed class PublicSiteResolution(SiteHostMapDataLayer map, ISiteContextWriter siteContextWriter) : IPublicSiteResolution
{
    public async Task<PublicSite?> ResolveAsync(string? hostHeader, CancellationToken cancellationToken)
    {
        if (!PublicHostNames.TryRead(hostHeader, out var host)
            || await map.FindAsync(host, cancellationToken) is not { } match)
        {
            return null;
        }

        siteContextWriter.Enter(match.SiteId);
        return new PublicSite(match.SiteId, host, match.CanonicalHost, match.IsCanonical, match.Status, match.Identity);
    }
}
