namespace PantryPress.Domain.Modules.Platform.Contracts;

/// <summary>
/// The platform-wide roles. <c>PlatformAdmin</c> is the only one (PP-D03); site roles come from membership, not from
/// Identity roles. The row is seeded by a migration, so no code path creates it at runtime (ACCT-005).
/// </summary>
public static class PlatformRoles
{
    public const string PlatformAdmin = "PlatformAdmin";

    /// <summary>Identity's normalized form, as <c>UpperInvariantLookupNormalizer</c> produces it.</summary>
    public const string PlatformAdminNormalized = "PLATFORMADMIN";

    /// <summary>The seeded row's key. Fixed, so the migration and every environment agree on it.</summary>
    public static readonly Guid PlatformAdminId = new("b2f1a6c4-7e5d-4a3b-9c21-5f0d8e4a7b31");
}

/// <summary>The named authorization policies for platform-wide work (auth.md).</summary>
public static class PlatformPolicies
{
    /// <summary>
    /// An operator: the <see cref="PlatformRoles.PlatformAdmin"/> role plus a session that passed the second sign-in
    /// step (<see cref="Accounts.Contracts.AccountClaims.TwoFactorVerified"/>).
    /// </summary>
    public const string PlatformAdmin = "PlatformAdmin";
}
