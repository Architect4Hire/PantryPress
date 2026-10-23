using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.DataLayer;
using PantryPress.Domain.Modules.Accounts.Repositories;
using PantryPress.Domain.Modules.Platform.Contracts;

namespace PantryPress.Domain.Modules.Accounts.Facades;

/// <summary>
/// Grants and revokes the platform role on an account (ACCT-005; PP1.4). It owns the unit of work and the security
/// stamp; who may ask, why, and the audit trail belong to the Platform module's facade.
/// </summary>
internal sealed class AccountRolesFacade(AccountsDataLayer data) : IAccountRoles
{
    public async Task<AccountId?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var user = await data.FindByEmailAsync((email ?? string.Empty).Trim());
        return user is null ? null : new AccountId(user.Id);
    }

    public Task<Result<bool>> GrantPlatformAdminAsync(AccountId account, CancellationToken cancellationToken) =>
        ChangeRoleAsync(account, grant: true, cancellationToken);

    public Task<Result<bool>> RevokePlatformAdminAsync(AccountId account, CancellationToken cancellationToken) =>
        ChangeRoleAsync(account, grant: false, cancellationToken);

    public async Task<Result<IReadOnlyList<AccountId>>> ListPlatformAdminsAsync(CancellationToken cancellationToken)
    {
        var holders = await data.GetAccountsInRoleAsync(PlatformRoles.PlatformAdmin);
        return Result<IReadOnlyList<AccountId>>.Success([.. holders.Select(u => new AccountId(u.Id))]);
    }

    private async Task<Result<bool>> ChangeRoleAsync(AccountId account, bool grant, CancellationToken cancellationToken)
    {
        var user = await data.FindAsync(account);
        if (user is null)
        {
            return AccountErrors.AccountNotFound;
        }

        if (await data.IsInRoleAsync(user, PlatformRoles.PlatformAdmin) == grant)
        {
            // Already as asked. Nothing is saved and the security stamp stays put, so a repeated run of the operator's
            // list never ends anyone's sessions.
            return false;
        }

        if (grant)
        {
            await data.AddToRoleAsync(user, PlatformRoles.PlatformAdmin);
        }
        else
        {
            await data.RemoveFromRoleAsync(user, PlatformRoles.PlatformAdmin);
        }

        // The role travels in the session's claims (PP1.6). Rotating the stamp ends the account's other sessions, so a
        // revoked operator cannot keep working with the cookie they already hold, and a new grant is picked up on the
        // next sign-in rather than half-applied.
        await data.RotateSecurityStampAsync(user);

        if (await data.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AccountErrors.ChangedElsewhere;
        }

        return true;
    }
}
