using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Accounts.Business;

namespace PantryPress.Domain.Modules.Accounts.Repositories;

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

    /// <summary>
    /// Saves a staged sign-out. Two sign-outs of one session can race - a retried request, two tabs - and the primary key
    /// lets one in; the other finds the session already revoked, which is exactly what it asked for.
    /// </summary>
    public async Task SaveRevocationAsync(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (!await db.Set<RevokedSession>().AsNoTracking().AnyAsync(r => r.SessionId == sessionId, cancellationToken))
            {
                throw;
            }
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
