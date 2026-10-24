namespace PantryPress.Domain.Managers.Idempotency;

/// <summary>How long keys are remembered and held, from the <c>Idempotency</c> configuration section.</summary>
public sealed class IdempotencyOptions
{
    /// <summary>
    /// How long a key is remembered after its first use (DECIDE at PP1.9: 24 hours). A request with the same key after
    /// this runs as new.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How long an unfinished claim holds the key before a retry may take it over. Long enough that a slow request is
    /// never run twice beside itself; short enough that a request lost to a crash does not keep its key answering
    /// "still in progress" for long.
    /// </summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(5);

    public bool IsValid() => Lease > TimeSpan.Zero && Retention > Lease;
}
