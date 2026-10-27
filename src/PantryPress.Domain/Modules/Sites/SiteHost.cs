using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites;

/// <summary>
/// One host a site answers on - its subdomain, a custom domain, or that domain's <c>www</c> variant - and whether it is
/// the one the others redirect to (SITE-005; PP2.9). Like the <see cref="Site"/> and its members it is part of the tenant
/// root: the public tier resolves a request's site from its Host through this table (tenancy.md rule 5), before any site
/// context exists, and a site's subdomain is written with the site, before there is a context to stamp from. So it
/// carries a required <see cref="SiteId"/> but is not <c>ISiteOwned</c> and has no query filter: every query is keyed by
/// a host or by a site already resolved, and only the Sites module reaches the table (tenancy.md, "Tenant root").
/// <para>
/// A host is unique across the platform, stored lower-case with no port or trailing dot (<c>Business.SiteHostNames</c>),
/// and never changes site or spelling: one changes hands by being removed and added again. Each site that is not deleted
/// keeps exactly one canonical host, which is active and never a <see cref="SiteHostKind.WwwVariant"/>.
/// </para>
/// </summary>
public sealed class SiteHost
{
    public SiteHostId Id { get; init; }

    public SiteId SiteId { get; init; }

    /// <summary>The host name, in the form <c>Business.SiteHostNames</c> stores.</summary>
    public string Host { get; init; } = string.Empty;

    public SiteHostKind Kind { get; set; }

    /// <summary>The host every other host of the site redirects to (SITE-005, DOM-003).</summary>
    public bool IsCanonical { get; set; }

    public SiteHostStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The concurrency token: the database moves it on every update, and a save from a stale read fails.</summary>
    public byte[] Version { get; set; } = [];
}
