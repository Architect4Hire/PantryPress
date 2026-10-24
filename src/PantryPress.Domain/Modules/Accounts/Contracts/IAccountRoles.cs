using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Results;

namespace PantryPress.Domain.Modules.Accounts.Contracts;

/// <summary>
/// The platform role on an account (ACCT-005; PP1.4). Deliberately narrow: it grants and revokes the one platform-wide
/// role and nothing else, there is no generic "add any role" call, and it is registered only in the operator's process
/// (<c>AddAccountRoles</c>), never in ApiService, Gateway, or Site. The Platform module is the only caller: it decides
/// who is asking and why, and a real change is audited with those, in the same save as the change (PP1.11).
/// </summary>
public interface IAccountRoles
{
    /// <summary>The account with this email address, or null for none. Casing and whitespace do not matter.</summary>
    Task<AccountId?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Gives the account the platform role. True when it did not have it; false when nothing changed, so a repeated
    /// grant is a no-op. A real grant also rotates the security stamp, which ends the account's other sessions.
    /// </summary>
    Task<Result<bool>> GrantPlatformAdminAsync(
        AccountId account,
        AuditActor actor,
        string reason,
        CancellationToken cancellationToken);

    /// <summary>
    /// Takes the platform role away. True when it was held; false when nothing changed. A real revocation rotates the
    /// security stamp, so a session that still carries the role claim stops working at its next check (PP1.6).
    /// </summary>
    Task<Result<bool>> RevokePlatformAdminAsync(
        AccountId account,
        AuditActor actor,
        string reason,
        CancellationToken cancellationToken);

    /// <summary>Every account that currently holds the platform role.</summary>
    Task<Result<IReadOnlyList<AccountId>>> ListPlatformAdminsAsync(CancellationToken cancellationToken);
}
