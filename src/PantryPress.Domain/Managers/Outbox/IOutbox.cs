using PantryPress.Domain.Managers.Tenancy;

namespace PantryPress.Domain.Managers.Outbox;

/// <summary>
/// Writes messages to the transactional outbox (backend.md, PP1.10). A message is staged on the request's own database
/// context and goes out with the next save on it: stage it immediately before the facade's one save - inside the
/// facade's transaction, if it has one - so the message commits with the change and is lost with it. A change that was
/// never saved sends nothing, and a saved one is delivered even if the process dies the moment after. A facade that
/// abandons its save after staging a message abandons the scope with it; a later save on the same context would still
/// carry the message. The Worker delivers each message at least once.
/// </summary>
public interface IOutbox
{
    /// <summary>Stages <paramref name="message"/> under a new id, and returns the id.</summary>
    /// <param name="site">
    /// <see cref="SiteScope.Current"/> for the request's own site, from <see cref="ISiteContext"/>, or
    /// <see cref="SiteScope.None"/> for account and platform work (account email), always said out loud (PP2.4). The
    /// handler re-resolves the site before touching site data, and a message type never carries a site of its own.
    /// </param>
    Guid Enqueue<TMessage>(TMessage message, SiteScope site)
        where TMessage : IOutboxMessage;

    /// <summary>
    /// Stages <paramref name="message"/> under an id the caller chose - one that already names the effect, such as an
    /// email's id, so the handler and the provider can both recognize a redelivery. An id derived from site-local values
    /// (a slug, a route) includes the site, or two sites' messages collide.
    /// </summary>
    void Enqueue<TMessage>(Guid messageId, TMessage message, SiteScope site)
        where TMessage : IOutboxMessage;
}
