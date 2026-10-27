using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Tenancy;

namespace PantryPress.Domain.Managers.Outbox;

/// <summary>
/// One message type the Worker can deliver, and how: read the payload as the message, enter the delivery's site, and hand
/// it to the type's <see cref="IOutboxHandler{TMessage}"/> from the delivery's scope. Registered by
/// <see cref="OutboxServiceCollectionExtensions.AddOutboxHandler{TMessage, THandler}"/>.
/// </summary>
public sealed class OutboxHandlerRegistration
{
    private readonly Func<IServiceProvider, string, OutboxDelivery, CancellationToken, Task> _invoke;

    private OutboxHandlerRegistration(
        string type,
        Type messageType,
        Func<IServiceProvider, string, OutboxDelivery, CancellationToken, Task> invoke)
    {
        Type = type;
        MessageType = messageType;
        _invoke = invoke;
    }

    /// <summary>The <see cref="IOutboxMessage.OutboxType"/> this registration delivers.</summary>
    public string Type { get; }

    public Type MessageType { get; }

    /// <summary>
    /// Delivers one payload. A payload that cannot be read as the message is a permanent failure: reading it again gives
    /// the same answer.
    /// </summary>
    public Task InvokeAsync(
        IServiceProvider services,
        string payload,
        OutboxDelivery delivery,
        CancellationToken cancellationToken) => _invoke(services, payload, delivery, cancellationToken);

    internal static OutboxHandlerRegistration For<TMessage>()
        where TMessage : IOutboxMessage
    {
        OutboxRules.EnsureValidMessageType<TMessage>();
        return new OutboxHandlerRegistration(TMessage.OutboxType, typeof(TMessage), async (services, payload, delivery, ct) =>
        {
            TMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<TMessage>(payload, OutboxRules.Json);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A message's own constructor can refuse what it reads, as well as the serializer.
                throw new OutboxPermanentFailureException("The payload does not read as its message type.", ex);
            }

            if (message is null)
            {
                throw new OutboxPermanentFailureException("The payload is empty.");
            }

            // The Worker's resolver (tenancy.md rules 2 and 10; PP2.11): a message staged with SiteScope.Current carries
            // the site its writer's request had resolved, and the delivery's scope works on that site and no other, so the
            // handler reads site-owned rows through the filter as the request that staged it would have. Account work
            // carries none and enters none.
            if (delivery.SiteId is { } site)
            {
                services.GetRequiredService<ISiteContextWriter>().Enter(site);
            }

            await services.GetRequiredService<IOutboxHandler<TMessage>>().HandleAsync(message, delivery, ct);
        });
    }
}
