using System.Text.Json;

namespace PantryPress.Domain.Managers.Idempotency;

/// <summary>
/// Remembers what a retried write already answered (API-005, POST-005; PP1.9). A caller sends a key with a write; the
/// first request with that key claims it and runs, and every later request from the same caller with the same key
/// gets the stored answer instead of running again - for <see cref="IdempotencyOptions.Retention"/>, after which the
/// key is new.
/// </summary>
/// <remarks>
/// The store keeps its own database context, never the request's: its saves must not commit something a facade staged
/// and then abandoned, and a facade's failure must not undo a claim.
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// Claims the key for this request, or says why not. Exactly one of any number of concurrent requests with the same
    /// caller and key is handed an <see cref="IdempotencyClaim.Acquired"/>.
    /// </summary>
    Task<IdempotencyClaim> ClaimAsync(IdempotencyRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the answer the claimed request produced. Does nothing if the claim was lost - its lease ran out and
    /// another request took the key over - so a late finisher never overwrites the answer of the one that replaced it.
    /// </summary>
    Task CompleteAsync(IdempotencyLease lease, StoredResponse response, CancellationToken cancellationToken);

    /// <summary>
    /// Gives the key back unanswered, so a retry runs again: the request failed in a way a retry might not
    /// (an unexpected error, a rate limit, a cancelled call). Does nothing if the claim was lost.
    /// </summary>
    Task ReleaseAsync(IdempotencyLease lease, CancellationToken cancellationToken);

    /// <summary>Deletes up to <paramref name="batchSize"/> records past their retention, and returns how many.</summary>
    Task<int> PurgeExpiredAsync(int batchSize, CancellationToken cancellationToken);
}

/// <summary>
/// One request that carries a key. <see cref="CallerScope"/> is who is asking - an account today, a publishing key at
/// PP18 - and keys never cross it. <see cref="Fingerprint"/> is a SHA-256 of what was asked, so the same key sent with a
/// different request is told apart from a retry.
/// </summary>
public sealed record IdempotencyRequest(string CallerScope, string Key, byte[] Fingerprint)
{
    public const int MaxCallerScopeLength = 80;
    public const int MaxKeyLength = 128;
    public const int FingerprintLength = 32;
}

/// <summary>What the claimed request answered: its status, the headers worth replaying, and its body.</summary>
public sealed record StoredResponse(int StatusCode, IReadOnlyDictionary<string, string> Headers, byte[] Body)
{
    /// <summary>The longest the headers may be once serialized, which is the column's length.</summary>
    public const int MaxHeadersLength = 4000;

    /// <summary>Whether the store can keep this answer whole. One that cannot is released rather than cut short.</summary>
    public bool FitsTheStore => JsonSerializer.Serialize(Headers).Length <= MaxHeadersLength;
}

/// <summary>Proof of holding a claim: the key, and the token that tells this claim apart from any that replaced it.</summary>
public sealed record IdempotencyLease(string CallerScope, string Key, Guid Token);

/// <summary>How a claim went. A closed set: one of the four records below.</summary>
public abstract record IdempotencyClaim
{
    private IdempotencyClaim()
    {
    }

    /// <summary>This request holds the key: run it, then complete or release the lease.</summary>
    public sealed record Acquired(IdempotencyLease Lease) : IdempotencyClaim;

    /// <summary>An earlier request with this key and this request's fingerprint already answered; replay it.</summary>
    public sealed record Completed(StoredResponse Response) : IdempotencyClaim;

    /// <summary>An earlier request with this key is still running; try again after <paramref name="RetryAfter"/>.</summary>
    public sealed record InProgress(TimeSpan RetryAfter) : IdempotencyClaim;

    /// <summary>The key was already used, within its retention, for a different request.</summary>
    public sealed record Mismatch : IdempotencyClaim;
}
