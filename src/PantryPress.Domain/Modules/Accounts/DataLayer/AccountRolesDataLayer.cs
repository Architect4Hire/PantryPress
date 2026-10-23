using Microsoft.AspNetCore.Identity;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.Repositories;

namespace PantryPress.Domain.Modules.Accounts.DataLayer;

/// <summary>
/// The only composition that changes a platform role (ARCH-002, ACCT-005). It is registered by
/// <c>AddAccountRoles</c> alone, so no request-serving host can resolve it, and the role-changing calls on Identity's
/// user manager are banned everywhere else (BannedSymbols.txt), which leaves <see cref="Contracts.IAccountRoles"/> as
/// the one way in. Nothing here saves except <see cref="SaveChangesAsync"/>.
/// </summary>
internal sealed class AccountRolesDataLayer(AccountUserManager users, AccountsUnitOfWork unitOfWork)
{
    public Task<ApplicationUser?> FindAsync(AccountId account) => users.FindByIdAsync(account.ToString());

    public Task<ApplicationUser?> FindByEmailAsync(string email) => users.FindByEmailAsync(email);

    public Task<bool> IsInRoleAsync(ApplicationUser user, string role) => users.IsInRoleAsync(user, role);

#pragma warning disable RS0030 // The one place PantryPress changes or lists a platform role (ACCT-005).

    /// <summary>Stages the role; the role row itself is seeded by a migration and never created here.</summary>
    public async Task AddToRoleAsync(ApplicationUser user, string role) => Ensure(await users.AddToRoleAsync(user, role));

    public async Task RemoveFromRoleAsync(ApplicationUser user, string role) =>
        Ensure(await users.RemoveFromRoleAsync(user, role));

    public Task<IList<ApplicationUser>> GetAccountsInRoleAsync(string role) => users.GetUsersInRoleAsync(role);

#pragma warning restore RS0030

    /// <summary>Stages a new security stamp, which ends the account's existing sessions once PP1.6 validates it.</summary>
    public async Task RotateSecurityStampAsync(ApplicationUser user) => Ensure(await users.UpdateSecurityStampAsync(user));

    public Task<SaveOutcome> SaveChangesAsync(CancellationToken cancellationToken) =>
        unitOfWork.SaveAsync(cancellationToken);

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Identity refused a platform-role change: {string.Join(", ", result.Errors.Select(e => e.Code))}.");
        }
    }
}
