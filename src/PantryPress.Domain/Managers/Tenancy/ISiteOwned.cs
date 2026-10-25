namespace PantryPress.Domain.Managers.Tenancy;

/// <summary>
/// An entity that belongs to one site (tenancy.md rule 1; ARCH-015): site-owned, published public, or reader-submitted
/// data (PP-D06). Its <see cref="SiteId"/> is never null and never set by hand: the global query filter reads the
/// request's <see cref="ISiteContext"/>, and the save stamps it from there (PP2.5). The tenant root - a site and its
/// memberships - and the operator tables are the written exceptions and do not implement this (tenancy.md).
/// </summary>
public interface ISiteOwned
{
    SiteId SiteId { get; }
}
