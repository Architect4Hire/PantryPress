using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.DataLayer;

namespace PantryPress.Domain.Modules.Accounts.Facades;

/// <inheritdoc cref="IAccountEmailConfirmation"/>
internal sealed class AccountEmailConfirmation(AccountsDataLayer data) : IAccountEmailConfirmation
{
    public async Task<bool> IsConfirmedAsync(AccountId account, CancellationToken cancellationToken) =>
        await data.FindAsync(account) is { EmailConfirmed: true };
}
