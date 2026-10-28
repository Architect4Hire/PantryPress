using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Business;

/// <summary>
/// What a site's status means to a reader (SITE-008, SEO-007; PP2.13). Live and read-only are open, since read-only is
/// how a failed payment starts and that is no reader's concern; a site being set up is coming soon; suspended and offline
/// are one answer, unavailable, so neither an operator's action nor a payment shows; a deleted site is gone.
/// </summary>
public static class PublicSiteStatus
{
    /// <summary>The answer a reader gets in place of the site, or null when the site is open to readers.</summary>
    public static Error? RefusalFor(SiteStatus status) => status switch
    {
        SiteStatus.Live or SiteStatus.ReadOnly => null,
        SiteStatus.Provisioning => SiteErrors.SiteComingSoon,
        SiteStatus.Suspended or SiteStatus.Offline => SiteErrors.SiteUnavailable,
        SiteStatus.Deleted => SiteErrors.SiteGone,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "A site status with no public answer."),
    };
}
