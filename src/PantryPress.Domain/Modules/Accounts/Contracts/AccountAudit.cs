using PantryPress.Domain.Managers.Audit;

namespace PantryPress.Domain.Modules.Accounts.Contracts;

/// <summary>
/// The Accounts module's audited actions (ACCT-005; PP1.11), each recorded with the change it describes. Every one is an
/// account action with no site. The codes are stable: the operator console reads them (OPS-006).
/// </summary>
public static class AccountAudit
{
    public const string PasswordChanged = "account.password-changed";
    public const string PasswordReset = "account.password-reset";
    public const string TwoFactorEnabled = "account.two-factor-enabled";
    public const string TwoFactorDisabled = "account.two-factor-disabled";
    public const string RecoveryCodesReplaced = "account.recovery-codes-replaced";
    public const string RecoveryCodeUsed = "account.recovery-code-used";

    /// <summary>The wrong password or code that locked the account; the attempts before it are not audited.</summary>
    public const string LockedOut = "account.locked-out";

    public const string PlatformAdminGranted = "account.platform-admin-granted";
    public const string PlatformAdminRevoked = "account.platform-admin-revoked";

    /// <summary>The target kind for an account.</summary>
    public const string TargetKind = "account";

    public static AuditTarget Target(AccountId account) => new(TargetKind, account.ToString());

    public static AuditActor Actor(AccountId account) => AuditActor.Account(account.Value);

    /// <summary>An account acting on itself, which is what a person's own security changes are.</summary>
    public static AuditEvent OwnChange(string action, AccountId account) => new(action, Actor(account), Target(account));
}
