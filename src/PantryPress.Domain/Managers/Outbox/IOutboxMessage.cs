namespace PantryPress.Domain.Managers.Outbox;

/// <summary>
/// A message a facade writes to the outbox (PP1.10), for a side effect that must survive a crash: an email, a purge, a
/// page-data build. Messages are small: ids and the few values the handler needs, serialized as JSON, never an entity.
/// </summary>
public interface IOutboxMessage
{
    /// <summary>
    /// The stable name stored with each message, which picks its handler. Renaming the class never strands queued
    /// messages; changing this name does, so it is a breaking change. Lowercase dot-separated segments, owner first,
    /// hyphens inside a segment (<c>email.send</c>, <c>rendering.build-page</c>).
    /// </summary>
    static abstract string OutboxType { get; }
}
