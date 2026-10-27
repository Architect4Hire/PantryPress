namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>
/// What a host is to its site (SITE-005). Stored as these numbers, so they never change; a new kind takes the next one.
/// </summary>
public enum SiteHostKind
{
    /// <summary>The site's address under the platform's zone, <c>{slug}.pantrypress.app</c> (SITE-003).</summary>
    Subdomain = 0,

    /// <summary>A domain the blogger brought, apex or <c>www</c> (DOM-001).</summary>
    Custom = 1,

    /// <summary>
    /// The other spelling of a custom domain - with <c>www.</c> or without it - which only redirects to that domain, so it
    /// is never canonical.
    /// </summary>
    WwwVariant = 2,
}
