using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Time;

namespace PantryPress.Domain.Managers.Idempotency;

/// <summary>
/// The SQL store for idempotency keys (PP1.9). The primary key on caller and key decides every race: a claim is an
/// insert, so two concurrent first requests cannot both win, and every other change is a conditional update or delete
/// on the lease token the caller observed, so two retries taking over the same abandoned claim cannot both win either.
/// </summary>
/// <remarks>
/// Each call opens its own scope and so its own context (see <see cref="IIdempotencyStore"/>).
/// </remarks>
internal sealed class IdempotencyStore(
    IServiceScopeFactory scopes,
    IClock clock,
    IOptions<IdempotencyOptions> options) : IIdempotencyStore
{
    // A claim can lose a race to an insert, then find the winner already released or taken over. Each lap observes a
    // newer state, so a few are plenty; past them the key is busy enough that "try again in a moment" is the truth.
    private const int MaxClaimAttempts = 4;

    public async Task<IdempotencyClaim> ClaimAsync(IdempotencyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        // One token for every lap: the connection's retry policy can re-send an insert that did land, and the next lap
        // must recognise that row as this request's own claim rather than someone else's.
        var token = Guid.NewGuid();
        for (var attempt = 0; attempt < MaxClaimAttempts; attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
            var records = db.Set<IdempotencyRecord>();
            var now = clock.UtcNow;

            var existing = await records.AsNoTracking()
                .SingleOrDefaultAsync(r => r.CallerScope == request.CallerScope && r.Key == request.Key, cancellationToken);

            if (existing is null)
            {
                records.Add(NewClaim(request, token, now));
                if (await TryInsertAsync(db, request, cancellationToken))
                {
                    return new IdempotencyClaim.Acquired(new IdempotencyLease(request.CallerScope, request.Key, token));
                }

                continue;
            }

            if (existing.LeaseToken == token && existing.State == IdempotencyState.InProgress)
            {
                return new IdempotencyClaim.Acquired(new IdempotencyLease(request.CallerScope, request.Key, token));
            }

            switch (IdempotencyRules.Decide(existing, request.Fingerprint, now))
            {
                case IdempotencyDecision.Replay:
                    return new IdempotencyClaim.Completed(ToResponse(existing));
                case IdempotencyDecision.Mismatch:
                    return new IdempotencyClaim.Mismatch();
                case IdempotencyDecision.Wait:
                    return new IdempotencyClaim.InProgress(IdempotencyRules.RetryAfter);
                case IdempotencyDecision.TakeOver:
                    var claim = NewClaim(request, token, now);
                    var taken = await records
                        .Where(r => r.CallerScope == request.CallerScope
                            && r.Key == request.Key
                            && r.LeaseToken == existing.LeaseToken
                            && r.State == existing.State)
                        .ExecuteUpdateAsync(
                            set => set
                                .SetProperty(r => r.Fingerprint, claim.Fingerprint)
                                .SetProperty(r => r.State, claim.State)
                                .SetProperty(r => r.LeaseToken, claim.LeaseToken)
                                .SetProperty(r => r.CreatedAt, claim.CreatedAt)
                                .SetProperty(r => r.LockedUntil, claim.LockedUntil)
                                .SetProperty(r => r.ExpiresAt, claim.ExpiresAt)
                                .SetProperty(r => r.ResponseStatus, (int?)null)
                                .SetProperty(r => r.ResponseHeaders, (string?)null)
                                .SetProperty(r => r.ResponseBody, (byte[]?)null),
                            cancellationToken);
                    if (taken == 1)
                    {
                        return new IdempotencyClaim.Acquired(new IdempotencyLease(request.CallerScope, request.Key, token));
                    }

                    continue;
            }
        }

        return new IdempotencyClaim.InProgress(IdempotencyRules.RetryAfter);
    }

    public async Task CompleteAsync(IdempotencyLease lease, StoredResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(response);
        var headers = JsonSerializer.Serialize(response.Headers);

        await using var scope = scopes.CreateAsyncScope();
        await Held(scope, lease).ExecuteUpdateAsync(
            set => set
                .SetProperty(r => r.State, IdempotencyState.Completed)
                .SetProperty(r => r.ResponseStatus, response.StatusCode)
                .SetProperty(r => r.ResponseHeaders, headers)
                .SetProperty(r => r.ResponseBody, response.Body),
            cancellationToken);
    }

    public async Task ReleaseAsync(IdempotencyLease lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        await using var scope = scopes.CreateAsyncScope();
        await Held(scope, lease).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> PurgeExpiredAsync(int batchSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        await using var scope = scopes.CreateAsyncScope();
        var now = clock.UtcNow;
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<IdempotencyRecord>()
            .Where(r => r.ExpiresAt <= now)
            .OrderBy(r => r.ExpiresAt)
            .Take(batchSize)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private IdempotencyRecord NewClaim(IdempotencyRequest request, Guid token, DateTimeOffset now) => new()
    {
        CallerScope = request.CallerScope,
        Key = request.Key,
        Fingerprint = request.Fingerprint,
        State = IdempotencyState.InProgress,
        LeaseToken = token,
        CreatedAt = now,
        LockedUntil = now + options.Value.Lease,
        ExpiresAt = now + options.Value.Retention,
    };

    /// <summary>The record only while this lease still holds it: unfinished, and with the token this claim was given.</summary>
    private static IQueryable<IdempotencyRecord> Held(AsyncServiceScope scope, IdempotencyLease lease) =>
        scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<IdempotencyRecord>()
            .Where(r => r.CallerScope == lease.CallerScope
                && r.Key == lease.Key
                && r.LeaseToken == lease.Token
                && r.State == IdempotencyState.InProgress);

    private static async Task<bool> TryInsertAsync(
        PantryPressDbContext db,
        IdempotencyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // The primary key is the last word on concurrent first requests. Asking whether a record now exists keeps
            // this provider-neutral: if one does, someone won (perhaps this request, on a retried insert) and the next
            // lap reads it; if none does, the insert failed for a reason that is not a race, and that is a bug to see.
            db.ChangeTracker.Clear();
            if (await db.Set<IdempotencyRecord>().AnyAsync(
                r => r.CallerScope == request.CallerScope && r.Key == request.Key,
                cancellationToken))
            {
                return false;
            }

            throw;
        }
    }

    private static void Validate(IdempotencyRequest request)
    {
        if (request.CallerScope.Length is 0 or > IdempotencyRequest.MaxCallerScopeLength)
        {
            throw new ArgumentException(
                $"An idempotency caller scope is 1 to {IdempotencyRequest.MaxCallerScopeLength} characters.",
                nameof(request));
        }

        if (request.Key.Length is 0 or > IdempotencyRequest.MaxKeyLength)
        {
            throw new ArgumentException(
                $"An idempotency key is 1 to {IdempotencyRequest.MaxKeyLength} characters.",
                nameof(request));
        }

        if (request.Fingerprint.Length != IdempotencyRequest.FingerprintLength)
        {
            throw new ArgumentException("An idempotency fingerprint is a SHA-256 hash.", nameof(request));
        }
    }

    private static StoredResponse ToResponse(IdempotencyRecord record) => new(
        record.ResponseStatus ?? throw new InvalidOperationException("A completed idempotency record has no status."),
        record.ResponseHeaders is { } headers
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(headers) ?? []
            : new Dictionary<string, string>(),
        record.ResponseBody ?? []);
}
