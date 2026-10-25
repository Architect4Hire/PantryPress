using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Managers.Time;

namespace PantryPress.Domain.Managers.Outbox;

/// <summary>
/// The SQL store behind <see cref="IOutboxStore"/>. A claim is one conditional update over candidates read just before
/// it: SQL Server re-checks the condition on each row after any lock wait, so of two Workers reaching for the same
/// message one updates it and the other finds it no longer due and skips it. Every later change is conditional on the
/// lease token, as in the idempotency store (PP1.9). Leases are timed by the Workers' own clocks, so skew between
/// Workers shortens or lengthens a lease by the skew.
/// </summary>
internal sealed class OutboxStore(
    IServiceScopeFactory scopes,
    IClock clock,
    IOptions<OutboxOptions> options) : IOutboxStore
{
    public async Task<IReadOnlyList<OutboxEnvelope>> ClaimAsync(
        IReadOnlyCollection<string> types,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        if (types.Count == 0)
        {
            return [];
        }

        await using var scope = scopes.CreateAsyncScope();
        var messages = Messages(scope);
        var now = clock.UtcNow;
        var leaseEnds = now + options.Value.Lease;
        var typeList = types.ToList();

        var candidates = await messages.AsNoTracking()
            .Where(m => m.State == OutboxState.Pending && m.AvailableAt <= now && typeList.Contains(m.Type))
            .OrderBy(m => m.AvailableAt)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return [];
        }

        // From here the claim runs to its end whatever the caller's token says: an update that commits after a stop is
        // requested must still be read back, or its rows sit leased with no Worker holding them until the lease ends.
        cancellationToken.ThrowIfCancellationRequested();

        // One token for the whole claim: the connection's retry policy can re-send an update that did land, and reading
        // back by token still finds what it took.
        var token = Guid.NewGuid();
        await messages
            .Where(m => candidates.Contains(m.Id) && m.State == OutboxState.Pending && m.AvailableAt <= now)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(m => m.LeaseToken, token)
                    .SetProperty(m => m.AvailableAt, leaseEnds),
                CancellationToken.None);

        var claimed = await messages.AsNoTracking()
            .Where(m => candidates.Contains(m.Id) && m.LeaseToken == token)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(CancellationToken.None);
        return claimed
            .Select(m => new OutboxEnvelope(
                new OutboxLease(m.Id, token),
                m.Type,
                m.Payload,
                m.SiteId is { } site ? new SiteId(site) : null,
                m.Attempts,
                m.TraceParent))
            .ToList();
    }

    public async Task<OutboxStart?> StartAsync(OutboxEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        await using var scope = scopes.CreateAsyncScope();
        var now = clock.UtcNow;
        var leaseEnds = now + options.Value.Lease;
        var lease = envelope.Lease;
        var before = envelope.Attempts;

        // Conditional on the attempt count the claim saw as well as the token, so a re-sent update that already landed
        // counts nothing twice; the read-back by token still finds the start.
        await Held(scope, lease)
            .Where(m => m.Attempts == before)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(m => m.AvailableAt, leaseEnds)
                    .SetProperty(m => m.Attempts, m => m.Attempts + 1),
                cancellationToken);

        var started = await Held(scope, lease)
            .Select(m => new { m.Attempts, m.AvailableAt })
            .SingleOrDefaultAsync(cancellationToken);
        return started is not null && started.Attempts == before + 1
            ? new OutboxStart(started.Attempts, started.AvailableAt)
            : null;
    }

    public async Task<bool> CompleteAsync(OutboxLease lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        await using var scope = scopes.CreateAsyncScope();
        var now = clock.UtcNow;
        return await Held(scope, lease).ExecuteUpdateAsync(
            set => set
                .SetProperty(m => m.State, OutboxState.Succeeded)
                .SetProperty(m => m.CompletedAt, now)
                .SetProperty(m => m.Payload, (string?)null)
                .SetProperty(m => m.LeaseToken, (Guid?)null)
                .SetProperty(m => m.LastError, (string?)null),
            cancellationToken) == 1;
    }

    public async Task<bool> RetryAsync(
        OutboxLease lease,
        DateTimeOffset availableAt,
        string error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var stored = Fit(error);
        await using var scope = scopes.CreateAsyncScope();
        return await Held(scope, lease).ExecuteUpdateAsync(
            set => set
                .SetProperty(m => m.AvailableAt, availableAt)
                .SetProperty(m => m.LeaseToken, (Guid?)null)
                .SetProperty(m => m.LastError, stored),
            cancellationToken) == 1;
    }

    public async Task<bool> PoisonAsync(OutboxLease lease, string error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var stored = Fit(error);
        await using var scope = scopes.CreateAsyncScope();
        var now = clock.UtcNow;
        return await Held(scope, lease).ExecuteUpdateAsync(
            set => set
                .SetProperty(m => m.State, OutboxState.Poisoned)
                .SetProperty(m => m.CompletedAt, now)
                .SetProperty(m => m.LeaseToken, (Guid?)null)
                .SetProperty(m => m.LastError, stored),
            cancellationToken) == 1;
    }

    public async Task<bool> ReleaseAsync(OutboxLease lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        await using var scope = scopes.CreateAsyncScope();
        var now = clock.UtcNow;
        return await Held(scope, lease).ExecuteUpdateAsync(
            set => set
                .SetProperty(m => m.AvailableAt, now)
                .SetProperty(m => m.LeaseToken, (Guid?)null),
            cancellationToken) == 1;
    }

    public async Task<int> PurgeAsync(int batchSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        await using var scope = scopes.CreateAsyncScope();
        var now = clock.UtcNow;
        var succeededBefore = now - options.Value.SucceededRetention;
        var poisonedBefore = now - options.Value.PoisonedRetention;
        return await Messages(scope)
            .Where(m => (m.State == OutboxState.Succeeded && m.CompletedAt <= succeededBefore)
                || (m.State == OutboxState.Poisoned && m.CompletedAt <= poisonedBefore))
            .OrderBy(m => m.CompletedAt)
            .Take(batchSize)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<TimeSpan> GetReadyAgeAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var now = clock.UtcNow;
        var oldest = await Messages(scope)
            .Where(m => m.State == OutboxState.Pending && m.AvailableAt <= now)
            .MinAsync(m => (DateTimeOffset?)m.AvailableAt, cancellationToken);
        return oldest is { } due ? now - due : TimeSpan.Zero;
    }

    private static DbSet<OutboxMessage> Messages(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<OutboxMessage>();

    /// <summary>The message only while this lease still holds it: pending, and with the token this claim was given.</summary>
    private static IQueryable<OutboxMessage> Held(AsyncServiceScope scope, OutboxLease lease) =>
        Messages(scope).Where(m => m.Id == lease.MessageId
            && m.LeaseToken == lease.Token
            && m.State == OutboxState.Pending);

    private static string Fit(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return error.Length <= OutboxRules.MaxErrorLength ? error : error[..OutboxRules.MaxErrorLength];
    }
}
