using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites;

/// <summary>
/// A blogger's site (SITE-001), which is the tenant (PP-D03). This is the tenant root, the one written exception to
/// tenancy.md rule 1: its <see cref="Id"/> is the <c>SiteId</c> every site-owned row carries, so it has no <c>SiteId</c>
/// of its own, is not <c>ISiteOwned</c>, and has no global query filter. It cannot have one: resolving a request's site
/// (from the route and membership, or from the Host through <c>SiteHost</c>) reads this table before any site context
/// exists. A site row is reached only through a slug or host that resolution has already authorized.
/// <para>
/// Design settings (PP11.1), the current theme snapshot (PP11.4), and the other site settings (SITE-006) arrive with
/// their own prompts. A site is never deleted by the app; <see cref="SiteStatus.Deleted"/> is a status.
/// </para>
/// </summary>
public sealed class Site
{
    public SiteId Id { get; init; }

    /// <summary>
    /// The site's address under pantrypress.app, unique across the platform (SITE-002). Always lower-case ASCII; the
    /// slug policy (<c>Business.SlugPolicy</c>, PP2.8) decides what is allowed, and the schema refuses anything outside its
    /// shape or on the reserved list.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Empty hides it.</summary>
    public string Tagline { get; set; } = string.Empty;

    public SiteStatus Status { get; set; }

    /// <summary>An IANA time zone id, applied for display and scheduling only (api-contract.md).</summary>
    public string TimeZone { get; set; } = string.Empty;

    /// <summary>A BCP 47 language tag.</summary>
    public string Locale { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The concurrency token: the database moves it on every update, and a save from a stale read fails.</summary>
    public byte[] Version { get; set; } = [];
}
