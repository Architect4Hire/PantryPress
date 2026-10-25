namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>
/// A member's role on one site (SITE-010, auth.md). Each covers the ones after it: an Owner may do what an Editor may,
/// and an Editor what an Author may. Stored as these numbers, so they never change.
/// </summary>
public enum SiteRole
{
    /// <summary>Everything, including billing, members, domains, and publishing keys.</summary>
    Owner = 0,

    /// <summary>All content, design, and moderation.</summary>
    Editor = 1,

    /// <summary>Their own posts only; cannot publish when the site requires review (POST-010).</summary>
    Author = 2,
}
