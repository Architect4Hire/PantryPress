using System.Security.Cryptography;

namespace PantryPress.Domain.Managers.Idempotency;

/// <summary>What to do with a key that already has a record.</summary>
internal enum IdempotencyDecision
{
    /// <summary>The earlier request with this fingerprint answered: replay it.</summary>
    Replay,

    /// <summary>The key was used for a different request and is still remembered.</summary>
    Mismatch,

    /// <summary>The earlier request is still running and its lease holds.</summary>
    Wait,

    /// <summary>The record is past its retention, or its claim was abandoned: this request takes the key.</summary>
    TakeOver,
}

/// <summary>The decisions over a key's existing record (PP1.9). No I/O; the store acts on the answer.</summary>
internal static class IdempotencyRules
{
    /// <remarks>
    /// The order matters. An expired record is forgotten whatever it held. A remembered key sent with a different
    /// request is a mismatch even when the earlier request was abandoned, because a client reusing a key for something
    /// else is a client bug, and taking the key over would hide it. Only then does the state decide.
    /// </remarks>
    public static IdempotencyDecision Decide(IdempotencyRecord existing, byte[] fingerprint, DateTimeOffset now)
    {
        if (now >= existing.ExpiresAt)
        {
            return IdempotencyDecision.TakeOver;
        }

        if (!CryptographicOperations.FixedTimeEquals(existing.Fingerprint, fingerprint))
        {
            return IdempotencyDecision.Mismatch;
        }

        if (existing.State == IdempotencyState.Completed)
        {
            return IdempotencyDecision.Replay;
        }

        return now >= existing.LockedUntil ? IdempotencyDecision.TakeOver : IdempotencyDecision.Wait;
    }

    /// <summary>
    /// How long a waiting retry should hold off. Not the rest of the lease: the lease is sized for a crash, and the
    /// request that holds it usually finishes in well under a second, after which the retry gets its answer.
    /// </summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(1);
}
