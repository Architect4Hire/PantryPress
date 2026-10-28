using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Domain.Modules.Sites.Business;

// How the Sites unit of work's saves end. They are the module's own vocabulary rather than the repositories', so a facade
// answers them without naming anything below its data layer (PP2.14).

/// <summary>How saving a new site ended.</summary>
internal enum NewSiteOutcome
{
    Saved,

    /// <summary>
    /// The slug or its host was taken, or reserved, between the check and the save. Nothing was saved.
    /// </summary>
    Unavailable,

    /// <summary>The account already owns as many sites as its limit allows. Nothing was saved.</summary>
    OverLimit,
}

/// <summary>How saving a new invitation ended (PP2.11, PP2.14).</summary>
internal enum InvitationOutcome
{
    Saved,

    /// <summary>The site has as many open invitations as it may, counted under the site's lock. Nothing was saved.</summary>
    TooMany,
}

/// <summary>How saving an accepted invitation ended (PP2.11).</summary>
internal enum AcceptanceOutcome
{
    Saved,

    /// <summary>The account became a member of the site some other way in the meantime. Nothing was saved.</summary>
    AlreadyMember,

    /// <summary>The invitation was used or replaced in the meantime. Nothing was saved.</summary>
    NoLongerOpen,
}

/// <summary>How saving a member's new role, or their removal, ended (PP2.11b).</summary>
internal enum MemberChangeOutcome
{
    Saved,

    /// <summary>The change would now leave the site with no active owner. Nothing was saved.</summary>
    LastOwner,

    /// <summary>The member was removed in the meantime, and the change was a new role. Nothing was saved.</summary>
    NotFound,

    /// <summary>Someone else changed the same member in the meantime. Nothing was saved.</summary>
    Changed,
}

/// <summary>At most <paramref name="Max"/> sites owned by <paramref name="Account"/>, the new one included.</summary>
internal readonly record struct OwnedSiteLimit(AccountId Account, int Max)
{
    /// <summary>
    /// Whether an account that owns <paramref name="owned"/> sites may own one more: the facade's first look and the unit of
    /// work's locked second look ask the same rule (PP2.14).
    /// </summary>
    public bool Allows(int owned) => owned < Max;
}
