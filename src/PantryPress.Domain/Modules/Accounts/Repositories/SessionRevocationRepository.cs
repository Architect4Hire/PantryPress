using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Persistence;

namespace PantryPress.Domain.Modules.Accounts.Repositories;

/// <summary>
/// The signed-out sessions (PP1.12). Reads are untracked and keyed; a revocation is staged for the unit of work to save
/// with <see cref="AccountsUnitOfWork.SaveRevocationAsync"/>, which treats a second sign-out of the same session as done.
/// </summary>
internal sealed class SessionRevocationRepository(PantryPressDbContext db)
{
    public Task<bool> IsRevokedAsync(string sessionId, CancellationToken cancellationToken) =>
        db.Set<RevokedSession>().AsNoTracking().AnyAsync(r => r.SessionId == sessionId, cancellationToken);

    public void Stage(RevokedSession revocation) => db.Set<RevokedSession>().Add(revocation);

    /// <summary>Deletes up to <paramref name="batchSize"/> revocations no session can outlive any more.</summary>
    public Task<int> PurgeExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) =>
        db.Set<RevokedSession>()
            .Where(r => r.ExpiresAt <= now)
            .OrderBy(r => r.ExpiresAt)
            .Take(batchSize)
            .ExecuteDeleteAsync(cancellationToken);
}
