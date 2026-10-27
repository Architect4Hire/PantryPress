using PantryPress.Domain.Managers.Tenancy;

namespace PantryPress.Domain.Managers.Outbox;

/// <summary>
/// Carries out one outbox message type in the Worker (PP1.10). Delivery is at least once and in no particular order: a
/// crash, a lost lease, or a lost acknowledgement delivers the same message again, so a handler is idempotent on
/// <see cref="OutboxDelivery.MessageId"/> - a second delivery has no second effect. An external effect passes the
/// message id to the provider as its idempotency key (an email's <c>Message-ID</c>); a database effect records the id in
/// the same transaction as the effect, or is a state change that is harmless to repeat (a pointer switch to one version).
/// </summary>
/// <remarks>
/// <para>
/// Returning completes the message. Throwing <see cref="OutboxPermanentFailureException"/> poisons it at once; any other
/// exception retries it with backoff until the attempts run out. Only the exception's type, and a permanent failure's
/// reason, is stored or logged. No provider call is made inside a database transaction (Template G).
/// </para>
/// <para>
/// Each delivery runs in its own service scope. The cancellation token fires when the Worker stops, and a tenth of the
/// lease before the lease ends; a handler cancelled for its lease is retried. Cancellation is cooperative, so a handler
/// that ignores the token can still be running when its lease ends and another Worker starts the message again - one
/// more reason the effect itself must be idempotent rather than guarded by a check-then-act.
/// </para>
/// </remarks>
public interface IOutboxHandler<in TMessage>
    where TMessage : IOutboxMessage
{
    Task HandleAsync(TMessage message, OutboxDelivery delivery, CancellationToken cancellationToken);
}

/// <summary>
/// Which delivery this is. <see cref="SiteId"/> is the site the writer's request had resolved (<c>SiteScope.Current</c>), or
/// null for account and platform work. The delivery's scope has already entered it when the handler runs
/// (<see cref="OutboxHandlerRegistration"/>, the Worker's resolver since the first site message, PP2.11), so site-owned rows
/// read through the filter as that site's (tenancy.md rule 10). The site may have changed since - deleted, or the row
/// the message names gone - so a handler still checks what it reads before acting on it.
/// </summary>
/// <param name="Attempt">1 for the first delivery, counting every delivery that started, including any lost to a crash.</param>
public sealed record OutboxDelivery(Guid MessageId, SiteId? SiteId, int Attempt);

/// <summary>
/// Thrown by a handler when retrying cannot help - the payload no longer makes sense, the target is gone for good. The
/// message is poisoned at once and kept for an operator. The reason is stored and logged, so it names no personal data.
/// </summary>
public sealed class OutboxPermanentFailureException(string reason, Exception? innerException = null)
    : Exception(reason, innerException);
