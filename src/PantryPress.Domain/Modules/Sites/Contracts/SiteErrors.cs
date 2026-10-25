using PantryPress.Domain.Managers.Results;

namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>The Sites module's expected failures. Codes are stable (api-contract.md); titles are what a blogger reads.</summary>
public static class SiteErrors
{
    /// <summary>
    /// No such site, or one the caller is not a member of: the two are one answer (tenancy.md rule 4), so a slug never
    /// tells a stranger whether a site exists.
    /// </summary>
    public static readonly Error SiteNotFound = new(
        ErrorKind.NotFound,
        "site-not-found",
        "We couldn't find that site.");

    /// <summary>A member whose role on the site does not reach what the route needs.</summary>
    public static readonly Error SiteRoleRequired = new(
        ErrorKind.Forbidden,
        "site-role-required",
        "Your role on this site doesn't include this. Ask the site's owner if you need it.");

    /// <summary>The change would leave the site with no active owner (SITE-010; PP2.3).</summary>
    public static readonly Error LastOwner = new(
        ErrorKind.Conflict,
        "last-owner",
        "A site needs at least one owner. Make someone else an owner first.");
}
