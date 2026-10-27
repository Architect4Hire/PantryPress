using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Business;

/// <summary>
/// A site's address under the platform's zone, <c>{slug}.{zone}</c> (SITE-003): <c>pantrypress.app</c> in production and
/// <c>localhost</c> in a local run (SITE-009). The zone is configuration, so this checks that every slug a site may take
/// gives a host <see cref="SiteHostNames"/> stores.
/// </summary>
public static class SiteSubdomains
{
    /// <summary>
    /// True when <paramref name="zone"/> is a zone subdomains can sit under: in stored form already (lower-case, no port,
    /// no dot at either end), and making a host of the longest slug.
    /// </summary>
    public static bool IsValidZone(string? zone) =>
        !string.IsNullOrEmpty(zone)
        && SiteHostNames.TryNormalize($"{new string('a', SiteLimits.MaxSlugLength)}.{zone}", out var host)
        && string.Equals(host, $"{new string('a', SiteLimits.MaxSlugLength)}.{zone}", StringComparison.Ordinal);

    /// <summary>
    /// The stored host for <paramref name="slug"/>, a slug in stored form the slug policy allowed, under
    /// <paramref name="zone"/>, which <see cref="IsValidZone"/> accepted. Anything else is a bug and throws.
    /// </summary>
    public static string For(string slug, string zone)
    {
        if (!SlugPolicy.IsWellFormed(slug, out var stored) || !string.Equals(stored, slug, StringComparison.Ordinal))
        {
            throw new ArgumentException("A subdomain is made from a slug in stored form that the slug policy allowed.", nameof(slug));
        }

        if (!IsValidZone(zone))
        {
            throw new ArgumentException("The subdomain zone is not a zone a host can sit under.", nameof(zone));
        }

        return $"{slug}.{zone}";
    }
}
