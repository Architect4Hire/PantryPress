namespace PantryPress.Domain.Managers.Outbox;

/// <summary>
/// The dispatcher's side of the outbox (PP1.10): claim, start, then complete, retry, poison, or release. Only the Worker
/// registers it. Every change after a claim is conditional on the lease token the claim handed out, so a delivery whose
/// lease ran out and was taken over changes nothing - its effect may have happened twice, which is why handlers are
/// idempotent, but its outcome is never recorded over the newer delivery's.
/// </summary>
/// <remarks>Each call opens its own scope and context; none of them shares a transaction with a handler.</remarks>
public interface IOutboxStore
{
    /// <summary>
    /// Leases up to <paramref name="batchSize"/> due messages of the given types, oldest first. A message is in at most
    /// one claim at a time, whichever Worker asks. A type outside <paramref name="types"/> is never claimed: a Worker that
    /// does not know it leaves it to one that does. Once the claim's update is sent it runs to its end, so a stop never
    /// leaves rows leased that no Worker was told about.
    /// </summary>
    Task<IReadOnlyList<OutboxEnvelope>> ClaimAsync(
        IReadOnlyCollection<string> types,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Starts the delivery of a claimed message: renews its lease from now, so time spent waiting in the batch costs it
    /// nothing, and counts the attempt - before the handler runs, so a message that kills the process still runs out.
    /// Null when the lease was lost while the message waited: another Worker owns it now, and this one must not run it.
    /// </summary>
    Task<OutboxStart?> StartAsync(OutboxEnvelope envelope, CancellationToken cancellationToken);

    /// <summary>Records the delivery and clears the payload. False when the lease was lost.</summary>
    Task<bool> CompleteAsync(OutboxLease lease, CancellationToken cancellationToken);

    /// <summary>Makes the message due again at <paramref name="availableAt"/>. False when the lease was lost.</summary>
    Task<bool> RetryAsync(OutboxLease lease, DateTimeOffset availableAt, string error, CancellationToken cancellationToken);

    /// <summary>Gives up on the message and keeps its payload for an operator. False when the lease was lost.</summary>
    Task<bool> PoisonAsync(OutboxLease lease, string error, CancellationToken cancellationToken);

    /// <summary>
    /// Hands the message back due now: the Worker is stopping. A message that never started costs nothing; one whose
    /// handler was cancelled mid-run keeps the attempt it was charged, so a handler that hangs until every stop still
    /// runs out. False when the lease was lost.
    /// </summary>
    Task<bool> ReleaseAsync(OutboxLease lease, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> finished messages past their retention, and returns how many. Pending
    /// messages are never purged.
    /// </summary>
    Task<int> PurgeAsync(int batchSize, CancellationToken cancellationToken);

    /// <summary>How long the oldest due message, of any type, has been waiting for a delivery; zero when none is.</summary>
    Task<TimeSpan> GetReadyAgeAsync(CancellationToken cancellationToken);
}

/// <summary>One claimed message: what to deliver, and the lease that proves the claim.</summary>
/// <param name="Payload">Null only for a malformed row, which the dispatcher poisons rather than stalls on.</param>
/// <param name="Attempts">Deliveries started before this claim.</param>
public sealed record OutboxEnvelope(
    OutboxLease Lease,
    string Type,
    string? Payload,
    Guid? SiteId,
    int Attempts,
    string? TraceParent);

/// <summary>A started delivery: its number, counting itself, and when its renewed lease ends.</summary>
public sealed record OutboxStart(int Attempt, DateTimeOffset LeaseExpiresAt);

/// <summary>Proof of holding a claim: the message, and the token that tells this claim apart from any that replaced it.</summary>
public sealed record OutboxLease(Guid MessageId, Guid Token);
