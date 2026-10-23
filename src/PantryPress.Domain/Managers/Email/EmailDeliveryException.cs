namespace PantryPress.Domain.Managers.Email;

/// <summary>
/// The provider did not accept a message. Gateways throw only this, never their provider's exceptions, and never
/// attach them: provider errors can quote the recipient. Transient failures are worth retrying; permanent ones (a
/// refused recipient) are not.
/// </summary>
public sealed class EmailDeliveryException(Guid messageId, bool isTransient, string reason)
    : Exception($"Email {messageId} was not delivered: {reason}")
{
    public Guid MessageId { get; } = messageId;

    public bool IsTransient { get; } = isTransient;
}
