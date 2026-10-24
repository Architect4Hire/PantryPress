namespace PantryPress.Domain.Managers.Idempotency;

internal enum IdempotencyState
{
    /// <summary>Claimed by a request that has not finished; held until <see cref="IdempotencyRecord.LockedUntil"/>.</summary>
    InProgress = 1,

    /// <summary>Answered; the response columns hold what to replay.</summary>
    Completed = 2,
}

/// <summary>
/// One key one caller sent (PP1.9). Not site-owned data (PP-D06): it belongs to the caller, and the caller is what
/// isolates it - the key is the primary key only together with <see cref="CallerScope"/>. A site route's path carries
/// the site, and the path is part of <see cref="Fingerprint"/>, so a key used on one site and then another is a
/// mismatch rather than a replay.
/// </summary>
internal sealed class IdempotencyRecord
{
    public string CallerScope { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public byte[] Fingerprint { get; set; } = [];

    public IdempotencyState State { get; set; }

    /// <summary>New for every claim, so a request whose claim was taken over cannot complete or release the new one.</summary>
    public Guid LeaseToken { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When an unfinished claim may be taken over by a retry (a crashed request never completes its own).</summary>
    public DateTimeOffset LockedUntil { get; set; }

    /// <summary>When the key is forgotten: a request after this runs as new, and the record may be purged.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    public int? ResponseStatus { get; set; }

    /// <summary>The replayable headers as a JSON object. Never cookies or the gateway's own headers.</summary>
    public string? ResponseHeaders { get; set; }

    public byte[]? ResponseBody { get; set; }
}
