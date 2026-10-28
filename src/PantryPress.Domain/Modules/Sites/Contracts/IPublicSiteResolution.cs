using PantryPress.Domain.Managers.Tenancy;

namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>
/// The site a public request's Host names (ARCH-003, SITE-005, PSEC-004; PP2.12).
/// </summary>
/// <param name="SiteId">The site, now the request's <see cref="ISiteContext"/>.</param>
/// <param name="Host">The host asked for, in stored form: lower-case, with no port or trailing dot.</param>
/// <param name="CanonicalHost">
/// The host the site's other hosts redirect to (SITE-005). A deleted site may have none left, and then this is
/// <paramref name="Host"/>: there is nowhere to send a reader.
/// </param>
/// <param name="IsCanonical">Whether <paramref name="Host"/> is that host; when it is not, a page answers 301 to it.</param>
/// <param name="Status">Where the site is in its life, which decides its public answer (SITE-008; PP2.13).</param>
public sealed record PublicSite(SiteId SiteId, string Host, string CanonicalHost, bool IsCanonical, SiteStatus Status);

/// <summary>
/// Resolves a public request's site from its Host header and nothing else (tenancy.md rule 5; PP2.12). The answer comes
/// from the host map in the cache once it is warm, so a warm request makes no SQL query; a miss reads <c>SiteHosts</c> once
/// and fills it. Every write to a site's hosts and every change of its status refreshes the map (tenancy.md, "Tenant
/// root"), so a host that changes hands resolves to its new site at once.
/// </summary>
public interface IPublicSiteResolution
{
    /// <summary>
    /// The site whose active host <paramref name="hostHeader"/> names, which becomes the request's
    /// <see cref="ISiteContext"/>; or null, entering nothing. Null alike for a host no site holds, a pending one, and a
    /// Host that is not a host name at all - an IP address in any spelling, a bad port, anything but ASCII - so a reader
    /// learns nothing from which it was. Those last are refused before the cache or the database is asked.
    /// </summary>
    /// <param name="hostHeader">The request's <c>Host</c> header exactly as it arrived, port included.</param>
    Task<PublicSite?> ResolveAsync(string? hostHeader, CancellationToken cancellationToken);
}
