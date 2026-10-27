namespace PantryPress.Domain.Modules.Sites.Contracts;

// Request and response records for the site routes (api-contract.md: records in Contracts/, never entities). Values
// arrive straight from JSON, so validators treat every property as possibly null. No record here carries a site id: a
// site is named by its slug in the route, and its id is minted on the server (tenancy.md rule 7, "Tenant root").

/// <summary>
/// Create a site, with its address under the platform's zone and the caller as its Owner (SITE-002, SITE-003; PP2.10).
/// </summary>
/// <param name="Slug">The address: <c>{slug}.pantrypress.app</c>. Compared case-insensitively and stored lower-case.</param>
/// <param name="Name">The site's name, 1 to 60 characters.</param>
/// <param name="Tagline">Up to 120 characters; empty or missing hides it.</param>
/// <param name="TimeZone">An IANA time zone id, such as <c>America/Chicago</c>.</param>
/// <param name="Locale">A BCP 47 language tag such as <c>en-US</c>; missing means <c>en-US</c>.</param>
public sealed record CreateSiteRequest(string Slug, string Name, string? Tagline, string TimeZone, string? Locale);

/// <summary>One page of the caller's sites, ordered by slug.</summary>
/// <param name="Limit">How many at most, 1 to 100; missing means 50.</param>
/// <param name="Cursor">The <see cref="SitePage.NextCursor"/> of the page before, or missing for the first.</param>
public sealed record ListSitesQuery(int? Limit, string? Cursor);

/// <summary>Whether a site could take this address now.</summary>
/// <param name="Slug">The address to ask about; compared case-insensitively.</param>
public sealed record SlugAvailabilityQuery(string Slug);

/// <summary>A site the caller is an active member of, as its admin app shows it.</summary>
/// <param name="Role">The caller's role there.</param>
/// <param name="Host">The host the site answers on and every other host of it redirects to (SITE-005).</param>
public sealed record SiteDetail(
    string Slug,
    string Name,
    string Tagline,
    SiteStatus Status,
    string TimeZone,
    string Locale,
    DateTimeOffset CreatedAt,
    SiteRole Role,
    string Host);

/// <summary>One of the caller's sites in a list.</summary>
public sealed record SiteSummary(string Slug, string Name, SiteStatus Status, SiteRole Role, string Host);

/// <summary>A page of the caller's sites; <paramref name="NextCursor"/> is null on the last.</summary>
public sealed record SitePage(IReadOnlyList<SiteSummary> Items, string? NextCursor);

/// <summary>
/// Whether a slug can be taken now. Reserved, held by another site, held by a deleted one, and still answering as another
/// site's address are all just "not available", so the answer never says why, let alone who holds it (SITE-002).
/// </summary>
/// <param name="Slug">The slug as it would be stored: lower-case.</param>
public sealed record SlugAvailability(string Slug, bool Available);
