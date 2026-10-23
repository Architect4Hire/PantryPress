using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Persistence;

namespace PantryPress.Domain.Modules.Accounts.Repositories;

/// <summary>How saving a unit of work ended.</summary>
internal enum SaveOutcome
{
    Saved,

    /// <summary>Someone else changed the account after it was read (its concurrency stamp moved). Nothing was saved.</summary>
    Conflict,

    /// <summary>A new account lost a race for its email address. Nothing was saved.</summary>
    EmailTaken,
}

/// <summary>
/// Commits what <see cref="AccountUserStore"/> staged for one facade call, and turns EF Core's save failures into
/// <see cref="SaveOutcome"/> values, so nothing above the repositories sees EF Core.
/// </summary>
internal sealed class AccountsUnitOfWork(PantryPressDbContext db, ILookupNormalizer normalizer)
{
    public async Task<SaveOutcome> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return SaveOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return SaveOutcome.Conflict;
        }
    }

    public async Task<SaveOutcome> SaveNewAccountAsync(string email, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return SaveOutcome.Saved;
        }
        catch (DbUpdateException)
        {
            // The unique email index is the last word on concurrent sign-ups (PP1.1). Asking again keeps this
            // provider-neutral: if the address now has an account, this sign-up lost the race.
            db.ChangeTracker.Clear();
            var normalized = normalizer.NormalizeEmail(email);
            if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken))
            {
                return SaveOutcome.EmailTaken;
            }

            throw;
        }
    }
}
