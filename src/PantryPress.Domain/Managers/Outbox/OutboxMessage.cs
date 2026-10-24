namespace PantryPress.Domain.Managers.Outbox;

internal enum OutboxState
{
    /// <summary>Waiting for delivery, or leased to a delivery in progress until <see cref="OutboxMessage.AvailableAt"/>.</summary>
    Pending = 1,

    /// <summary>Delivered; the payload is cleared and the row kept a short while for operators.</summary>
    Succeeded = 2,

    /// <summary>Gave up: a permanent failure or the last attempt spent. The payload is kept for an operator.</summary>
    Poisoned = 3,
}

/// <summary>
/// One message in the transactional outbox (PP1.10). Not site-owned data (PP-D06): the dispatcher reads every site's
/// messages by design, and no admin or public route reads this table at all. <see cref="SiteId"/> is carried to the
/// handler, which re-resolves the site from it.
/// </summary>
internal sealed class OutboxMessage
{
    public Guid Id { get; set; }

    /// <summary>The message's <see cref="IOutboxMessage.OutboxType"/>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The message as JSON; cleared once delivered, since it may name a person (an email's recipient).</summary>
    public string? Payload { get; set; }

    public Guid? SiteId { get; set; }

    public OutboxState State { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the next delivery may start. A claim, and again the start of its delivery, moves it to the end of the lease,
    /// so a delivery lost to a crash becomes claimable again by itself once the lease runs out.
    /// </summary>
    public DateTimeOffset AvailableAt { get; set; }

    /// <summary>New for every claim, so a delivery whose lease was taken over cannot complete or fail the new one.</summary>
    public Guid? LeaseToken { get; set; }

    /// <summary>Deliveries started, counted before the handler runs so a message that kills the process still runs out.</summary>
    public int Attempts { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Why the last delivery failed: an exception type name or a handler's stated reason, never its message text.</summary>
    public string? LastError { get; set; }

    /// <summary>The W3C trace parent of the request that wrote the message, so its delivery joins the same trace.</summary>
    public string? TraceParent { get; set; }
}
