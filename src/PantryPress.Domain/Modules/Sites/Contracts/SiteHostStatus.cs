namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>
/// Whether a host answers for its site. Only an active host resolves, and only an active one can be canonical. Stored as
/// these numbers, so they never change; the custom-domain states DOM-005 needs arrive with PP17 and take the next ones.
/// </summary>
public enum SiteHostStatus
{
    Active = 0,

    /// <summary>A custom domain waiting for its verification (DOM-001). It holds the host but resolves nothing.</summary>
    Pending = 1,
}
