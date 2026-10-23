namespace PantryPress.Domain.Modules.Accounts.Contracts;

/// <summary>
/// Claim types PantryPress adds to a session beyond Identity's own. The BFF session that carries them arrives with
/// PP1.6; PP1.4 defines them so the authorization policies can already require them.
/// </summary>
public static class AccountClaims
{
    /// <summary>
    /// Present only when this session passed the second sign-in step. The <c>PlatformAdmin</c> policy requires it
    /// (auth.md: two-step sign-in is required for operators), so a stolen password alone never reaches operator work.
    /// </summary>
    public const string TwoFactorVerified = "pp:mfa";

    /// <summary>The one value <see cref="TwoFactorVerified"/> is ever issued with.</summary>
    public const string Yes = "true";
}
