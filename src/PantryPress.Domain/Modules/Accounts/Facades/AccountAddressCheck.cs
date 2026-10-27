using PantryPress.Domain.Managers.Email;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.DataLayer;

namespace PantryPress.Domain.Modules.Accounts.Facades;

/// <inheritdoc cref="IAccountAddressCheck"/>
internal sealed class AccountAddressCheck(AccountsDataLayer data) : IAccountAddressCheck
{
    public async Task<AccountAddressMatch> MatchAsync(AccountId account, string email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        // Identity's user lookups take no cancellation token. A gone account holds no address, and the answer for it is
        // the same as for someone else's: the caller asked about its own account and learns nothing more.
        var user = await data.FindAsync(account);
        if (user?.Email is not { } own || !EmailAddresses.IsValid(own) || !EmailAddresses.IsValid(email)
            || !string.Equals(EmailAddresses.Normalize(own), EmailAddresses.Normalize(email), StringComparison.Ordinal))
        {
            return AccountAddressMatch.OtherAddress;
        }

        return user.EmailConfirmed ? AccountAddressMatch.Confirmed : AccountAddressMatch.Unconfirmed;
    }
}
