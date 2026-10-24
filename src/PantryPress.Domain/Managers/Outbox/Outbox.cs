using System.Diagnostics;
using System.Text.Json;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Time;

namespace PantryPress.Domain.Managers.Outbox;

/// <summary>
/// The scoped <see cref="IOutbox"/>: adds each message to the request's own context and does no I/O, so the message
/// shares the facade's save and its fate. The message stays tracked until a save carries it or the scope ends.
/// </summary>
internal sealed class Outbox(PantryPressDbContext db, IClock clock) : IOutbox
{
    public Guid Enqueue<TMessage>(TMessage message, Guid? siteId)
        where TMessage : IOutboxMessage
    {
        var id = Guid.NewGuid();
        Enqueue(id, message, siteId);
        return id;
    }

    public void Enqueue<TMessage>(Guid messageId, TMessage message, Guid? siteId)
        where TMessage : IOutboxMessage
    {
        // Every refusal here is a bug in the writer, never something a person did.
        ArgumentNullException.ThrowIfNull(message);
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("An outbox message needs an id.", nameof(messageId));
        }

        if (siteId == Guid.Empty)
        {
            throw new ArgumentException("A site id is a site's id or null for platform work.", nameof(siteId));
        }

        OutboxRules.EnsureValidMessageType<TMessage>();

        var payload = JsonSerializer.Serialize(message, OutboxRules.Json);
        if (payload.Length > OutboxRules.MaxPayloadLength)
        {
            throw new ArgumentException(
                $"An outbox message is at most {OutboxRules.MaxPayloadLength} characters as JSON; carry references, not data.",
                nameof(message));
        }

        var now = clock.UtcNow;
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = messageId,
            Type = TMessage.OutboxType,
            Payload = payload,
            SiteId = siteId,
            State = OutboxState.Pending,
            CreatedAt = now,
            AvailableAt = now,
            TraceParent = Activity.Current is { IdFormat: ActivityIdFormat.W3C, Id: { Length: <= OutboxRules.MaxTraceParentLength } id }
                ? id
                : null,
        });
    }
}
