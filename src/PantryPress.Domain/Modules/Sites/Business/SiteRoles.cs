using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Business;

/// <summary>What each site role covers, and the rule that every site keeps an owner (SITE-010; PP2.3).</summary>
public static class SiteRoles
{
    /// <summary>True when <paramref name="held"/> may do what <paramref name="required"/> may: Owner, then Editor, then Author.</summary>
    public static bool Covers(SiteRole held, SiteRole required)
    {
        Require(held);
        Require(required);

        // Owner is 0 and Author is 2, so the stronger role is the smaller number.
        return held <= required;
    }

    /// <summary>
    /// <see cref="SiteErrors.LastOwner"/> when giving a member <paramref name="newRole"/> would leave the site with no
    /// active owner. <paramref name="activeOwners"/> counts the site's active owners now, the member included if they are one.
    /// </summary>
    public static Result CanChangeRole(SiteRole role, SiteMemberStatus status, SiteRole newRole, int activeOwners) =>
        KeepsAnOwner(IsActiveOwner(role, status), IsActiveOwner(newRole, status), activeOwners);

    /// <summary><see cref="SiteErrors.LastOwner"/> when suspending the member would leave the site with no active owner.</summary>
    public static Result CanSuspend(SiteRole role, SiteMemberStatus status, int activeOwners) =>
        KeepsAnOwner(IsActiveOwner(role, status), stillActiveOwner: false, activeOwners);

    /// <summary>
    /// <see cref="SiteErrors.LastOwner"/> when removing the member - or the member leaving - would leave the site with no
    /// active owner.
    /// </summary>
    public static Result CanRemove(SiteRole role, SiteMemberStatus status, int activeOwners) =>
        KeepsAnOwner(IsActiveOwner(role, status), stillActiveOwner: false, activeOwners);

    private static bool IsActiveOwner(SiteRole role, SiteMemberStatus status) =>
        role == SiteRole.Owner && status == SiteMemberStatus.Active;

    private static Result KeepsAnOwner(bool activeOwner, bool stillActiveOwner, int activeOwners)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(activeOwners);
        if (activeOwner && activeOwners < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(activeOwners), "An active owner is counted among the active owners.");
        }

        return activeOwner && !stillActiveOwner && activeOwners == 1 ? SiteErrors.LastOwner : Result.Success;
    }

    private static void Require(SiteRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Not a site role.");
        }
    }
}
