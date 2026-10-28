namespace PantryPress.Domain.Modules.Sites.Contracts;

// Response records for the public site routes (api-contract.md: records in Contracts/, never entities). No record here
// carries a site id: the reader's Host already named the site (tenancy.md rules 5 and 7).

/// <summary>
/// A site as the public tier describes it (SITE-008, SEO-007; PP2.13): what the site record calls it and where it lives,
/// and nothing private. These are the site record's values, not the rendered brand: once the theme snapshot carries
/// <c>brand.siteName</c> and <c>brand.tagline</c> (PP11.4), a page takes its branding from the snapshot alone.
/// </summary>
/// <param name="Name">The site's name, as the blogger wrote it.</param>
/// <param name="Tagline">Empty hides it.</param>
/// <param name="Locale">A BCP 47 language tag, such as <c>en-US</c>.</param>
/// <param name="CanonicalHost">
/// The host the site answers on and its other hosts redirect to (SITE-005). The host, not a URL: the scheme and port are
/// the edge's, and a local run's differ.
/// </param>
/// <param name="IsCanonical">Whether the request's own host is <paramref name="CanonicalHost"/>; when not, a page answers 301.</param>
public sealed record PublicSiteDetail(string Name, string Tagline, string Locale, string CanonicalHost, bool IsCanonical);
