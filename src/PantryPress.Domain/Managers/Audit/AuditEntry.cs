namespace PantryPress.Domain.Managers.Audit;

/// <summary>
/// One row of the append-only audit log (PP1.11). Not site-owned data (PP-D06): the operator reads every site's
/// entries by design (OPS-006), and no admin or public route reads this table at all. Written once, never changed or
/// removed.
/// </summary>
internal sealed class AuditEntry
{
    /// <summary>An identity column, so the clustered key grows in append order.</summary>
    public long Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string Action { get; set; } = string.Empty;

    /// <summary>The actor kind's code (<see cref="AuditRules.ActorKindCode"/>).</summary>
    public string ActorKind { get; set; } = string.Empty;

    /// <summary>The acting account's id; null for every other kind.</summary>
    public Guid? ActorId { get; set; }

    public Guid? SiteId { get; set; }

    public string TargetKind { get; set; } = string.Empty;

    public string TargetId { get; set; } = string.Empty;

    public string? Reason { get; set; }

    /// <summary>The W3C trace id of the request or job that did the action, to find its logs.</summary>
    public string? TraceId { get; set; }
}
