using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.DataLayer;

namespace PantryPress.Domain.Modules.Accounts.Facades;

/// <inheritdoc cref="IAccountDirectory"/>
internal sealed class AccountDirectory(AccountsDataLayer data) : IAccountDirectory
{
    public async Task<IReadOnlyDictionary<AccountId, string>> FindAddressesAsync(
        IReadOnlyCollection<AccountId> accounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        if (accounts.Count > IAccountDirectory.MaxAccounts)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accounts),
                accounts.Count,
                $"Ask for at most {IAccountDirectory.MaxAccounts} accounts at a time.");
        }

        var found = await data.FindAddressesAsync([.. accounts.Select(a => a.Value).Distinct()], cancellationToken);
        return found.ToDictionary(f => new AccountId(f.Key), f => f.Value);
    }
}
