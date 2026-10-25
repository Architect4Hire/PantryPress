namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>
/// Where a site is in its life (SITE-001), each with its own public answer (sites.md, SITE-008). Stored as these
/// numbers, so they never change; a new status takes the next one.
/// </summary>
public enum SiteStatus
{
    Provisioning = 0,
    Live = 1,
    Suspended = 2,
    ReadOnly = 3,
    Offline = 4,
    Deleted = 5,
}
