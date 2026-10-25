namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>
/// Whether a membership grants access. A suspended member is kept, with their role, but reaches nothing - which is what
/// a downgrade over the member limit needs, since a downgrade never deletes (BILL-005). Removing a member deletes the
/// membership; the audit log keeps the history. Stored as these numbers, so they never change.
/// </summary>
public enum SiteMemberStatus
{
    Active = 0,
    Suspended = 1,
}
