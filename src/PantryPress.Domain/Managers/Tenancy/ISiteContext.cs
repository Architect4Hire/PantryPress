namespace PantryPress.Domain.Managers.Tenancy;

/// <summary>
/// The site this request or job works on (tenancy.md rule 2), set once and never changed. An admin request gets it from
/// its route's slug and the caller's membership (rule 4); nothing a client sends can name it (rule 7). A request with no
/// site - an account route, a refused one - has none, and site work is a bug there.
/// </summary>
public interface ISiteContext
{
    /// <summary>The site, or null when this request works on none.</summary>
    SiteId? Current { get; }

    /// <summary>The site; throws when there is none, because site work without a site is a bug, never a person's doing.</summary>
    SiteId Required { get; }
}

/// <summary>
/// Sets the <see cref="ISiteContext"/>. Only the code that resolved the site may hold this: the Sites module's facades,
/// which resolve it from the route and membership (PP2.4), and later the public tier's host resolution and the Worker's
/// site jobs, each declared in <c>DomainBoundaryTests</c> when it is added. Internal, so no host can name it at all: every
/// resolver lives in the domain.
/// </summary>
internal interface ISiteContextWriter
{
    /// <summary>
    /// Makes <paramref name="site"/> this scope's site. Entering the same site again changes nothing; entering another
    /// throws, because one request works on one site.
    /// </summary>
    void Enter(SiteId site);
}
