using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Persistence;

namespace PantryPress.Domain.Modules.Recipes.Repositories;

/// <summary>
/// Commits what one facade call staged, inside the retry strategy and one transaction, so nothing above the repositories
/// sees EF Core (backend.md, "The transaction, as built"). The decisions are the facade's; this owns only how they are
/// committed.
/// </summary>
internal sealed class RecipesUnitOfWork(PantryPressDbContext db)
{
    /// <summary>
    /// Runs <paramref name="attempt"/> inside a transaction, again on each retry, and commits what it staged. Each attempt
    /// starts with nothing tracked, so a retry stages its own rows and never one a failed attempt left behind; an attempt
    /// that takes a lock - <c>RecipeRepository.FindLockedAsync</c> - holds it to the commit, so what it decided under the lock
    /// still holds when it is saved. Changes are accepted only once the transaction commits. An attempt stages only what it
    /// means to save: one that answers a refusal or a repeat stages nothing, and nothing is saved.
    /// <para>
    /// The database runs read-committed snapshot (<c>MigrationWorker</c>): reads after a lock see only committed rows, so an
    /// attempt that locks a recipe and then reads its parts reads a whole recipe, never an edit's uncommitted half (PP4.4).
    /// A save that breaks a key or index - which a lock-guarded rule makes unreachable - is a bug, and throws.
    /// </para>
    /// </summary>
    public async Task<T> RunLockedAsync<T>(Func<CancellationToken, Task<T>> attempt, CancellationToken cancellationToken)
    {
        var saved = false;
        T answer;
        try
        {
            answer = await db.Database.CreateExecutionStrategy().ExecuteAsync(
                async token =>
                {
                    db.ChangeTracker.Clear();
                    saved = false;
                    await using var transaction = await db.Database.BeginTransactionAsync(token);
                    var result = await attempt(token);
                    if (db.ChangeTracker.HasChanges())
                    {
                        await db.SaveChangesAsync(acceptAllChangesOnSuccess: false, token);
                        saved = true;
                    }

                    await transaction.CommitAsync(token);
                    return result;
                },
                cancellationToken);
        }
        catch
        {
            // Nothing staged may go out with a later save, whatever ended this one.
            db.ChangeTracker.Clear();
            throw;
        }

        if (saved)
        {
            db.ChangeTracker.AcceptAllChanges();
        }
        else
        {
            db.ChangeTracker.Clear();
        }

        return answer;
    }
}
