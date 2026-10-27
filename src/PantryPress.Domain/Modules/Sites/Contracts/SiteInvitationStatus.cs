namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>
/// Where an invitation stands (SITE-010; PP2.11). An invitation is open while it is <see cref="Pending"/> and not past its
/// expiry; expiry is a time, not a status, so nothing has to run for an invitation to stop working. Stored as these
/// numbers, so they never change.
/// </summary>
public enum SiteInvitationStatus
{
    Pending = 0,

    /// <summary>Used: someone joined the site with it. An invitation is accepted once.</summary>
    Accepted = 1,

    /// <summary>Replaced by a newer invitation to the same address, so the older link stops working.</summary>
    Revoked = 2,
}
