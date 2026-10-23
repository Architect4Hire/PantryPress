namespace PantryPress.Domain.Managers.Email;

/// <summary>
/// Hands an email to be sent after the caller's changes are saved, off the request path, so a response never waits on
/// (or reveals anything through the timing of) a send. Until the PP1.10 outbox the queue lives in process: a message
/// queued just before a crash is lost, and the user's recovery is to ask again (resend, forgot password).
/// </summary>
public interface IEmailQueue
{
    /// <summary>Queues the message; never blocks and never throws for a full queue (it drops and logs the id).</summary>
    void Enqueue(EmailMessage message);
}
