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

    /// <summary>
    /// How long an invitation's link works if nobody uses it (PP2.11): seven days, approved at PP2.11. Inviting the
    /// address again replaces it with a fresh one.
    /// </summary>
    public TimeSpan InvitationLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How many open invitations one site may have at once (PP2.11), so an Owner cannot use a site to send email to
    /// address after address. Accepted, replaced, and expired ones do not count. PP14's member limit comes on top.
    /// </summary>
    public int MaxOpenInvitations { get; set; } = 50;

    /// <summary>At least an hour and at most 30 days: a link that outlives a month is a standing key to the site.</summary>
    public bool HasValidInvitationLifetime() =>
        InvitationLifetime >= TimeSpan.FromHours(1) && InvitationLifetime <= TimeSpan.FromDays(30);

    /// <summary>Lower-case, with no port and no dot at either end, and long enough for the longest slug.</summary>
    public bool HasValidZone() => SiteSubdomains.IsValidZone(SubdomainZone);
}
