using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.DataLayer;

namespace PantryPress.Domain.Modules.Accounts.Facades;

/// <summary>The Accounts module's housekeeping, run by the Worker's sweep (PP1.12).</summary>
internal sealed class AccountMaintenanceFacade(AccountsDataLayer data, IClock clock) : IAccountMaintenance
{
    public Task<int> PurgeExpiredSessionRevocationsAsync(int batchSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        return data.PurgeExpiredRevocationsAsync(clock.UtcNow, batchSize, cancellationToken);
    }
}
