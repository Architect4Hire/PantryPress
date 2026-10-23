namespace PantryPress.Domain.Managers.Email;

/// <summary>
/// Sends transactional email (ARCH-011, external.md). Locally a development sink delivers to a mail catcher; Azure
/// Communication Services sits behind this interface from PP13.1. Implementations never log recipients or bodies.
/// </summary>
public interface IEmailGateway
{
    /// <summary>
    /// Hands one message to the provider. Throws <see cref="EmailDeliveryException"/> when the provider does not
    /// accept it, so a caller that must not lose the email retries transient failures or goes through the outbox.
    /// Cancellation by the caller surfaces as <see cref="OperationCanceledException"/>.
    /// </summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
