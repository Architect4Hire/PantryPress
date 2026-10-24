using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.DataLayer;
using PantryPress.Domain.Modules.Accounts.Repositories;

namespace PantryPress.Domain.Modules.Accounts.Facades;

/// <summary>
/// Grants and revokes the platform role on an account (ACCT-005; PP1.4). It owns the unit of work and the security
/// stamp, so it records the audit entry the change is saved with (PP1.11); who may ask and why belong to the Platform
/// module's facade, which passes them in.
/// </summary>
internal sealed class AccountRolesFacade(AccountRolesDataLayer data, IAuditLog audit) : IAccountRoles
{
    public async Task<AccountId?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var user = await data.FindByEmailAsync((email ?? string.Empty).Trim());
        return user is null ? null : new AccountId(user.Id);
    }

    public Task<Result<bool>> GrantPlatformAdminAsync(
        AccountId account,
        AuditActor actor,
        string reason,
        CancellationToken cancellationToken) =>
        ChangeRoleAsync(account, grant: true, actor, reason, cancellationToken);

    public Task<Result<bool>> RevokePlatformAdminAsync(
        AccountId account,
        AuditActor actor,
        string reason,
        CancellationToken cancellationToken) =>
        ChangeRoleAsync(account, grant: false, actor, reason, cancellationToken);

    public async Task<Result<IReadOnlyList<AccountId>>> ListPlatformAdminsAsync(CancellationToken cancellationToken)
    {
        var holders = await data.GetAccountsInRoleAsync(AccountRoles.PlatformAdmin);
        return Result<IReadOnlyList<AccountId>>.Success([.. holders.Select(u => new AccountId(u.Id))]);
    }

    private async Task<Result<bool>> ChangeRoleAsync(
        AccountId account,
        bool grant,
        AuditActor actor,
        string reason,
        CancellationToken cancellationToken)
    {
        var user = await data.FindAsync(account);
        if (user is null)
        {
            return AccountErrors.AccountNotFound;
        }

        // Checked before anything is staged, so a refused entry can never leave a role change waiting on the context.
        var auditEvent = new AuditEvent(
            grant ? AccountAudit.PlatformAdminGranted : AccountAudit.PlatformAdminRevoked,
            actor,
            AccountAudit.Target(account),
            reason);
        AuditRules.EnsureValid(auditEvent, siteId: null);

        if (await data.IsInRoleAsync(user, AccountRoles.PlatformAdmin) == grant)
        {
            // Already as asked. Nothing is saved and the security stamp stays put, so a repeated run of the operator's
            // list never ends anyone's sessions.
            return false;
        }

        if (grant)
        {
            await data.AddToRoleAsync(user, AccountRoles.PlatformAdmin);
        }
        else
        {
            await data.RemoveFromRoleAsync(user, AccountRoles.PlatformAdmin);
        }

        // The role travels in the session's claims (PP1.6). Rotating the stamp ends the account's other sessions, so a
        // revoked operator cannot keep working with the cookie they already hold, and a new grant is picked up on the
        // next sign-in rather than half-applied.
        await data.RotateSecurityStampAsync(user);

        audit.Record(auditEvent, siteId: null);
        if (await data.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AccountErrors.ChangedElsewhere;
        }

        return true;
    }
}
