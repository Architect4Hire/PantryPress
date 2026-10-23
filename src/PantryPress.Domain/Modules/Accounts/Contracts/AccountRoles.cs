namespace PantryPress.Domain.Modules.Accounts.Contracts;

/// <summary>
/// The Identity role rows Accounts stores. Accounts owns Identity, so it owns the name on the row; what the role
/// means, who may hold it, and the audit trail belong to the Platform module (<c>PlatformRoles</c>), which takes its
/// name from here. There is one role: site roles come from membership, not from Identity (PP-D03).
/// </summary>
public static class AccountRoles
{
    public const string PlatformAdmin = "PlatformAdmin";

    /// <summary>Identity's normalized form, as <c>UpperInvariantLookupNormalizer</c> produces it.</summary>
    public const string PlatformAdminNormalized = "PLATFORMADMIN";
}
