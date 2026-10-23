using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Domain.Modules.Platform.Contracts;

/// <summary>
/// Granting and revoking <c>PlatformAdmin</c> (ACCT-005; PP1.4). Every change is audited with who asked and why. There
/// is no route to this facade and no self-service path: it is registered only in the operator's process, and the
/// operator drives it through <see cref="IPlatformAdminReconciler"/>. The operator console (PP19) will add its own
/// audited surface on top.
/// </summary>
public interface IPlatformAdminFacade
{
    /// <summary>
    /// Gives the account with this email address the platform role. <see cref="AccountErrors.AccountNotFound"/> when no
    /// account has the address, which is normal for someone who has not signed up yet.
    /// </summary>
    Task<Result<PlatformAdminOutcome>> GrantAsync(
        string email,
        string actor,
        string reason,
        CancellationToken cancellationToken);

    Task<Result<PlatformAdminOutcome>> RevokeAsync(
        AccountId account,
        string actor,
        string reason,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<AccountId>>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>
/// What a grant or revocation did. <see cref="Changed"/> is false when the account was already as asked, so running
/// the operator's list again changes nothing and audits nothing.
/// </summary>
public sealed record PlatformAdminOutcome(AccountId Account, bool Changed);

/// <summary>Who asked for a platform-role change, for the audit line.</summary>
public static class PlatformAdminActors
{
    /// <summary>The operator's configured list, applied by the migration job.</summary>
    public const string OperatorConfiguration = "operator:configuration";
}

/// <summary>
/// Applies the operator's configured list of platform administrators (ACCT-005): the one assignment path in PP1.4. It
/// runs in the migration job, which the operator starts; nothing about it is reachable from a browser or an API.
/// </summary>
public interface IPlatformAdminReconciler
{
    Task<PlatformAdminReconcileReport> RunAsync(CancellationToken cancellationToken);
}

/// <summary>
/// What one run of the list did. <see cref="Unresolved"/> counts addresses with no account yet: a warning, not a
/// failure, because the next run picks them up once the person has signed up.
/// </summary>
public sealed record PlatformAdminReconcileReport(bool Configured, int Granted, int Revoked, int Unresolved)
{
    /// <summary>No list in configuration: nothing was read and nothing was changed.</summary>
    public static PlatformAdminReconcileReport NotConfigured { get; } = new(false, 0, 0, 0);
}
