using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Persistence;

namespace PantryPress.Domain.Modules.Accounts.Repositories;

/// <summary>
/// Accounts' addresses by id, for <c>IAccountDirectory</c> (PP2.11b): one untracked read of two columns, keyed by the ids
/// given, so nothing else about an account is loaded.
/// </summary>
internal sealed class AccountAddressRepository(PantryPressDbContext db)
{
    public async Task<IReadOnlyDictionary<Guid, string>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var rows = await db.Set<ApplicationUser>()
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.Email != null)
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id, r => r.Email!);
    }
}
