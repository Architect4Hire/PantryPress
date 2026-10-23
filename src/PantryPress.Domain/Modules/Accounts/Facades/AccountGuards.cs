using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.DataLayer;
using PantryPress.Domain.Modules.Accounts.Repositories;

namespace PantryPress.Domain.Modules.Accounts.Facades;

/// <summary>Checks shared by the facades that ask for a password or code before a security change.</summary>
internal static class AccountGuards
{
    /// <summary>
    /// Null when the password is right. A wrong one counts toward lockout and is saved straight away, so a failed
    /// request still leaves its mark. A locked account is refused before the password is checked.
    /// </summary>
    public static async Task<Error?> CheckPasswordAsync(
        AccountsDataLayer data,
        ApplicationUser user,
        string password,
        CancellationToken cancellationToken)
    {
        if (await data.IsLockedOutAsync(user))
        {
            return AccountErrors.TooManyAttempts;
        }

        if (await data.CheckPasswordAsync(user, password))
        {
            return null;
        }

        return await FailAsync(data, user, AccountErrors.IncorrectPassword, cancellationToken);
    }

    /// <summary>Records a wrong password or code and returns the error to show, which becomes lockout at the limit.</summary>
    public static async Task<Error> FailAsync(
        AccountsDataLayer data,
        ApplicationUser user,
        Error error,
        CancellationToken cancellationToken)
    {
        await data.RecordFailedAttemptAsync(user);
        if (await data.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AccountErrors.ChangedElsewhere;
        }

        return await data.IsLockedOutAsync(user) ? AccountErrors.TooManyAttempts : error;
    }
}
