using PantryPress.Domain.Managers.Audit;
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
        IAuditLog audit,
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

        return await FailAsync(data, audit, user, AccountErrors.IncorrectPassword, cancellationToken);
    }

    /// <summary>
    /// Records a wrong password or code and returns the error to show, which becomes lockout at the limit. The attempt
    /// that locks the account is audited with the save (PP1.11); PantryPress locks it, so the actor is the system.
    /// </summary>
    public static async Task<Error> FailAsync(
        AccountsDataLayer data,
        IAuditLog audit,
        ApplicationUser user,
        Error error,
        CancellationToken cancellationToken)
    {
        var wasLockedOut = await data.IsLockedOutAsync(user);
        await data.RecordFailedAttemptAsync(user);
        if (!wasLockedOut && await data.IsLockedOutAsync(user))
        {
            var account = new AccountId(user.Id);
            audit.Record(new AuditEvent(AccountAudit.LockedOut, AuditActor.System, AccountAudit.Target(account)), siteId: null);
        }

        if (await data.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AccountErrors.ChangedElsewhere;
        }

        return await data.IsLockedOutAsync(user) ? AccountErrors.TooManyAttempts : error;
    }
}
