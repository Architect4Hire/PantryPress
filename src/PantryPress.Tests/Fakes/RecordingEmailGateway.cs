using System.Collections.Concurrent;
using PantryPress.Domain.Managers.Email;

namespace PantryPress.Tests.Fakes;

// The in-memory fake for transactional email (external.md): records what a test sent, or fails on demand the way a
// real gateway does, with EmailDeliveryException.
internal sealed class RecordingEmailGateway : IEmailGateway
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyList<EmailMessage> Sent => [.. _sent];

    public EmailDeliveryException? FailWith { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailWith is not null)
        {
            return Task.FromException(FailWith);
        }

        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
