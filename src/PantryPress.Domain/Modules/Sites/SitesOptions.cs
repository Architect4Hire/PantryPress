using PantryPress.Domain.Modules.Sites.Business;

namespace PantryPress.Domain.Modules.Sites;

/// <summary>Settings for the Sites module, from the <c>Sites</c> configuration section.</summary>
public sealed class SitesOptions
{
    public const string SectionName = "Sites";

    /// <summary>
    /// The zone every site's own address sits under, <c>{slug}.{zone}</c> (SITE-003): <c>pantrypress.app</c> in production,
    /// <c>dev.pantrypress.app</c> in the dev deployment, and <c>localhost</c> in a local run (SITE-009). Set by the AppHost;
    /// never derived from a request, so nothing a caller sends chooses where a new site answers.
    /// </summary>
    public string SubdomainZone { get; set; } = string.Empty;

    /// <summary>
    /// How many sites an account whose email address is not confirmed may own, deleted ones included (PP2.10). A slug is
    /// never reissued, so this is what stops throwaway accounts taking addresses; confirming the address lifts it, and
    /// PP14's entitlements cap the rest. Zero means no site at all before confirming.
    /// </summary>
    public int UnconfirmedAccountSiteLimit { get; set; } = 1;

    /// <summary>Lower-case, with no port and no dot at either end, and long enough for the longest slug.</summary>
    public bool HasValidZone() => SiteSubdomains.IsValidZone(SubdomainZone);
}
