using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.DataLayer;

namespace PantryPress.Domain.Modules.Accounts.Facades;

/// <summary>ACCT-001: an account publishes only once its email address is confirmed.</summary>
internal sealed class AccountPublishingEligibility(AccountsDataLayer data) : IAccountPublishingEligibility
{
    public async Task<Result> CheckAsync(AccountId account, CancellationToken cancellationToken)
    {
        var user = await data.FindAsync(account);
        if (user is null)
        {
            return AccountErrors.AccountNotFound;
        }

        return user.EmailConfirmed ? Result.Success : AccountErrors.EmailNotVerified;
    }
}
