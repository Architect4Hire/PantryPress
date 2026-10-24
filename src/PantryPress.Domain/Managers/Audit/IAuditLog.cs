namespace PantryPress.Domain.Managers.Audit;

/// <summary>
/// Writes the append-only audit log (ACCT-005, OPS-006; PP1.11): who did a security-relevant or operator action, to
/// what, on which site, and when. An entry is staged on the request's own database context and goes out with the next
/// save on it, exactly as an outbox message does: record it immediately before the facade's one save, so the entry
/// commits with the change and is lost with it. Record only a change that really happens - a repeat that changes
/// nothing records nothing. Validate whatever the entry depends on before staging the change itself, so a refused entry
/// cannot strand a staged change. A save that fails leaves the entry tracked beside the change it describes, on purpose:
/// they stay together, and a caller that gives up on a save gives up on the scope with it, as with the outbox.
/// <para>
/// There is no way to change or remove an entry, here or anywhere: the context refuses to save one that was modified
/// or deleted, and the table's trigger refuses it in the database. Nothing reads the log yet; the operator console
/// does (PP19).
/// </para>
/// </summary>
public interface IAuditLog
{
    /// <summary>Stages <paramref name="auditEvent"/>, timed by the application clock.</summary>
    /// <param name="siteId">
    /// The site the action belongs to, or <c>null</c> for an account or platform action, always said out loud.
    /// </param>
    void Record(AuditEvent auditEvent, Guid? siteId);
}

/// <summary>
/// One audited action. Every part of it is an id or a code, never a person's details: no email address, name, IP
/// address, or content (PP1.11's restriction).
/// </summary>
/// <param name="Action">A stable code, lowercase dot-separated segments such as <c>account.password-changed</c>.</param>
/// <param name="Reason">
/// Why, when someone gave a reason: short plain text, which must not name anyone. An email address is refused.
/// </param>
public sealed record AuditEvent(string Action, AuditActor Actor, AuditTarget Target, string? Reason = null);
