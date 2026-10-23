using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PantryPress.Domain.Managers.Email;

/// <summary>
/// The in-process <see cref="IEmailQueue"/>: a bounded channel drained by <see cref="EmailDispatchWorker"/>. Replaced by
/// the transactional outbox in PP1.10.
/// </summary>
internal sealed partial class InProcessEmailQueue(ILogger<InProcessEmailQueue> logger) : IEmailQueue
{
    // A burst beyond this is far past any rate limit in front of the queue; dropping is safer than unbounded memory.
    private const int Capacity = 1_000;

    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public void Enqueue(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!_channel.Writer.TryWrite(message))
        {
            LogDropped(logger, message.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {MessageId} was dropped: the send queue is full.")]
    private static partial void LogDropped(ILogger logger, Guid messageId);
}

/// <summary>
/// Sends queued email through <see cref="IEmailGateway"/>, retrying transient failures a few times. Logs only message
/// ids, never recipients or bodies (backend.md).
/// </summary>
internal sealed partial class EmailDispatchWorker(
    InProcessEmailQueue queue,
    IEmailGateway gateway,
    TimeProvider timeProvider,
    ILogger<EmailDispatchWorker> logger) : BackgroundService
{
    internal static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await SendWithRetriesAsync(message, stoppingToken);
        }
    }

    private async Task SendWithRetriesAsync(EmailMessage message, CancellationToken stoppingToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await gateway.SendAsync(message, stoppingToken);
                return;
            }
            catch (EmailDeliveryException ex) when (ex.IsTransient && attempt < RetryDelays.Length)
            {
                LogRetrying(logger, message.Id, attempt + 1);
                await Task.Delay(RetryDelays[attempt], timeProvider, stoppingToken);
            }
            catch (EmailDeliveryException ex)
            {
                // The exception text names only the message id; the gateway keeps provider details to itself.
                LogFailed(logger, ex, message.Id);
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email {MessageId} hit a transient failure; retry {Retry}.")]
    private static partial void LogRetrying(ILogger logger, Guid messageId, int retry);

    [LoggerMessage(Level = LogLevel.Error, Message = "Email {MessageId} was not sent.")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid messageId);
}
