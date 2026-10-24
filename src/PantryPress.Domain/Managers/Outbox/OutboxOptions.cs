namespace PantryPress.Domain.Managers.Outbox;

/// <summary>How the Worker claims, retries, and forgets outbox messages, from the <c>Outbox</c> configuration section.</summary>
public sealed class OutboxOptions
{
    /// <summary>Messages claimed at once. A full batch is followed straight away by the next claim.</summary>
    public int BatchSize { get; set; } = 16;

    /// <summary>Messages of one batch handled side by side.</summary>
    public int MaxParallelism { get; set; } = 4;

    /// <summary>
    /// How long a claim holds a message. A handler still running at the end is cancelled, and a delivery lost to a crash is
    /// claimed again once it passes, so it bounds both a handler's run and how long a crash delays a message.
    /// </summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The pause between claims when the last one found less than a full batch (jittered).</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Deliveries before a message is poisoned. With the default backoff the twelve span roughly one to two hours, long
    /// enough to ride out a provider's outage.
    /// </summary>
    public int MaxAttempts { get; set; } = 12;

    /// <summary>The delay after the first failure; it doubles after each further one, up to <see cref="RetryCap"/>.</summary>
    public TimeSpan RetryBase { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan RetryCap { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How long a delivered message's row is kept for operators before it is purged.</summary>
    public TimeSpan SucceededRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>How long a poisoned message, payload included, is kept for an operator before it is purged.</summary>
    public TimeSpan PoisonedRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How often the Worker purges expired rows and samples the backlog.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(1);

    public int PurgeBatchSize { get; set; } = 500;

    public bool IsValid() =>
        BatchSize is > 0 and <= 100
        && MaxParallelism > 0
        && Lease > TimeSpan.Zero
        && PollInterval > TimeSpan.Zero
        && MaxAttempts > 0
        && RetryBase > TimeSpan.Zero
        && RetryCap >= RetryBase
        && SucceededRetention > TimeSpan.Zero
        && PoisonedRetention > TimeSpan.Zero
        && SweepInterval > TimeSpan.Zero
        && PurgeBatchSize is > 0 and <= 5_000;
}
