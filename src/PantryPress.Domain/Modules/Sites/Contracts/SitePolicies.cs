namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>
/// The named authorization policies for site work (auth.md), each the least role a route needs on the site its route
/// names. Checked in controllers; facades still re-check what a role may touch (an Author editing someone else's post).
/// </summary>
public static class SitePolicies
{
    public const string SiteOwner = "SiteOwner";
    public const string SiteEditor = "SiteEditor";
    public const string SiteAuthor = "SiteAuthor";

    /// <summary>Each policy and the least role it admits.</summary>
    public static IReadOnlyDictionary<string, SiteRole> LeastRole { get; } = new Dictionary<string, SiteRole>(StringComparer.Ordinal)
    {
        [SiteOwner] = SiteRole.Owner,
        [SiteEditor] = SiteRole.Editor,
        [SiteAuthor] = SiteRole.Author,
    };
}
