using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Managers.Time;

namespace PantryPress.Domain.Managers.Audit;

/// <summary>
/// The scoped <see cref="IAuditLog"/>: adds each entry to the request's own context and does no I/O, so the entry
/// shares the facade's save and its fate. The entry stays tracked until a save carries it or the scope ends.
/// </summary>
internal sealed class AuditLog(PantryPressDbContext db, IClock clock, ISiteContext siteContext) : IAuditLog
{
    public void Record(AuditEvent auditEvent, SiteScope site)
    {
        AuditRules.EnsureValid(auditEvent, site);
        var siteId = siteContext.Resolve(site);

        db.Set<AuditEntry>().Add(new AuditEntry
        {
            OccurredAt = clock.UtcNow,
            Action = auditEvent.Action,
            ActorKind = AuditRules.ActorKindCode(auditEvent.Actor.Kind),
            ActorId = auditEvent.Actor.Id,
            SiteId = siteId?.Value,
            TargetKind = auditEvent.Target.Kind,
            TargetId = auditEvent.Target.Id,
            Reason = auditEvent.Reason,
            TraceId = Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity
                ? activity.TraceId.ToHexString()
                : null,
        });
    }

    /// <summary>
    /// Refuses a save that would change or remove an audit entry: the log is append-only (PP1.11). Called by the
    /// context before every save; reading the entries runs change detection, so a property edited in place is caught.
    /// Bulk updates and deletes never pass through here, and the table's trigger refuses those.
    /// </summary>
    internal static void EnsureAppendOnly(ChangeTracker changeTracker)
    {
        if (changeTracker.Entries<AuditEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("The audit log is append-only: an entry is never changed or removed.");
        }
    }
}
