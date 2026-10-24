using System.Collections.Frozen;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Managers.Time;

namespace PantryPress.Worker.Outbox;

/// <summary>
/// One pass of the dispatcher (PP1.10): claim a batch of the types this Worker handles, start and deliver each message in
/// its own scope, and record how it went. Driven by <see cref="OutboxDispatcher"/>, and directly by tests.
/// </summary>
/// <remarks>
/// Recording an outcome is conditional on the lease, and a failure to record one leaves the lease to run out, so the
/// worst any failure here does is deliver a message again - never lose it, and never record over a newer delivery.
/// Store errors are database errors and are logged in full; a handler's failure is logged by its description only.
/// </remarks>
internal sealed partial class OutboxProcessor
{
    private readonly IOutboxStore _store;
    private readonly IServiceScopeFactory _scopes;
    private readonly IClock _clock;
    private readonly TimeProvider _time;
    private readonly OutboxOptions _options;
    private readonly OutboxMetrics _metrics;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly FrozenDictionary<string, OutboxHandlerRegistration> _handlers;

    public OutboxProcessor(
        IOutboxStore store,
        IServiceScopeFactory scopes,
        IEnumerable<OutboxHandlerRegistration> registrations,
        IClock clock,
        TimeProvider time,
        IOptions<OutboxOptions> options,
        OutboxMetrics metrics,
        ILogger<OutboxProcessor> logger)
    {
        var handlers = new Dictionary<string, OutboxHandlerRegistration>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            if (!handlers.TryAdd(registration.Type, registration))
            {
                throw new InvalidOperationException(
                    $"Two outbox handlers are registered for '{registration.Type}' ({handlers[registration.Type].MessageType.Name} " +
                    $"and {registration.MessageType.Name}). A message type has exactly one handler.");
            }
        }

        _handlers = handlers.ToFrozenDictionary(StringComparer.Ordinal);
        _store = store;
        _scopes = scopes;
        _clock = clock;
        _time = time;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>The message types this Worker claims.</summary>
    public IReadOnlyCollection<string> Types => _handlers.Keys;

    /// <summary>
    /// Claims one batch and delivers it, and returns how many messages were claimed. Every claimed message is recorded,
    /// released, or left to the Worker that took it over before this returns, even when <paramref name="stoppingToken"/>
    /// fires.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken stoppingToken)
    {
        var batch = await _store.ClaimAsync(Types, _options.BatchSize, stoppingToken);
        if (batch.Count == 0)
        {
            return 0;
        }

        // The loop itself is never cancelled: a message that is claimed but not started must still be released.
        await Parallel.ForEachAsync(
            batch,
            new ParallelOptions { MaxDegreeOfParallelism = _options.MaxParallelism },
            async (envelope, _) => await ProcessAsync(envelope, stoppingToken));
        return batch.Count;
    }

    private async Task ProcessAsync(OutboxEnvelope envelope, CancellationToken stoppingToken)
    {
        try
        {
            await DeliverAsync(envelope, stoppingToken);
        }
        catch (Exception ex)
        {
            // The store could not record the outcome. The lease runs out and the message is delivered again.
            LogRecordFailed(_logger, ex, envelope.Lease.MessageId, envelope.Type);
        }
    }

    private async Task DeliverAsync(OutboxEnvelope envelope, CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            await ReleaseAsync(envelope);
            return;
        }

        if (envelope.Payload is not { } payload)
        {
            await PoisonAsync(envelope, envelope.Attempts, "The message has no payload.");
            return;
        }

        // The lease is renewed and the attempt counted only now, as the delivery starts: waiting behind slower messages
        // in the batch costs nothing, and a message another Worker took over meanwhile is left to it.
        if (await _store.StartAsync(envelope, CancellationToken.None) is not { } start)
        {
            LogLeaseLost(_logger, envelope.Lease.MessageId, envelope.Type);
            return;
        }

        if (OutboxRules.IsPastLastAttempt(start.Attempt, _options))
        {
            await PoisonAsync(envelope, start.Attempt, "The last attempt never finished.");
            return;
        }

        // The handler is cancelled a tenth of the lease before it ends, leaving the rest for recording the outcome while
        // the lease still holds.
        var handlerTime = start.LeaseExpiresAt - (_options.Lease / 10) - _clock.UtcNow;
        var handler = _handlers[envelope.Type];
        using var leaseEnd = new CancellationTokenSource(handlerTime > TimeSpan.Zero ? handlerTime : TimeSpan.Zero, _time);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, leaseEnd.Token);
        using var activity = StartActivity(envelope, start.Attempt);
        var started = _time.GetTimestamp();

        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            await handler.InvokeAsync(
                scope.ServiceProvider,
                payload,
                new OutboxDelivery(envelope.Lease.MessageId, envelope.SiteId, start.Attempt),
                cancellation.Token);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await ReleaseAsync(envelope);
            return;
        }
        catch (Exception ex)
        {
            // A handler that ran out of lease lands here too: cancelled, and retried like any other failure.
            activity?.SetStatus(ActivityStatusCode.Error);
            var error = OutboxRules.Describe(ex);
            if (ex is OutboxPermanentFailureException || OutboxRules.IsLastAttempt(start.Attempt, _options))
            {
                await PoisonAsync(envelope, start.Attempt, error);
            }
            else
            {
                await RetryAsync(envelope, start.Attempt, error);
            }

            return;
        }

        if (await _store.CompleteAsync(envelope.Lease, CancellationToken.None))
        {
            _metrics.Delivered(envelope.Type, _time.GetElapsedTime(started));
            LogDelivered(_logger, envelope.Lease.MessageId, envelope.Type, start.Attempt);
        }
        else
        {
            LogLeaseLost(_logger, envelope.Lease.MessageId, envelope.Type);
        }
    }

    private async Task RetryAsync(OutboxEnvelope envelope, int attempt, string error)
    {
        var delay = OutboxRules.RetryDelay(attempt, _options, Random.Shared.NextDouble());
        if (await _store.RetryAsync(envelope.Lease, _clock.UtcNow + delay, error, CancellationToken.None))
        {
            _metrics.Retried(envelope.Type);
            LogRetrying(_logger, envelope.Lease.MessageId, envelope.Type, attempt, error, delay);
        }
        else
        {
            LogLeaseLost(_logger, envelope.Lease.MessageId, envelope.Type);
        }
    }

    private async Task PoisonAsync(OutboxEnvelope envelope, int attempt, string error)
    {
        if (await _store.PoisonAsync(envelope.Lease, error, CancellationToken.None))
        {
            _metrics.Poisoned(envelope.Type);
            LogPoisoned(_logger, envelope.Lease.MessageId, envelope.Type, attempt, error);
        }
        else
        {
            LogLeaseLost(_logger, envelope.Lease.MessageId, envelope.Type);
        }
    }

    private async Task ReleaseAsync(OutboxEnvelope envelope)
    {
        if (await _store.ReleaseAsync(envelope.Lease, CancellationToken.None))
        {
            _metrics.Released(envelope.Type);
            LogReleased(_logger, envelope.Lease.MessageId, envelope.Type);
        }
    }

    /// <summary>A consumer span, under the trace of the request that wrote the message when it carried one.</summary>
    private static Activity? StartActivity(OutboxEnvelope envelope, int attempt)
    {
        _ = ActivityContext.TryParse(envelope.TraceParent, traceState: null, isRemote: true, out var parent);
        var activity = OutboxMetrics.ActivitySource.StartActivity($"outbox {envelope.Type}", ActivityKind.Consumer, parent);
        activity?.SetTag("outbox.type", envelope.Type);
        activity?.SetTag("outbox.message_id", envelope.Lease.MessageId);
        activity?.SetTag("outbox.attempt", attempt);
        return activity;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Outbox message {MessageId} ({Type}) was delivered on attempt {Attempt}.")]
    private static partial void LogDelivered(ILogger logger, Guid messageId, string type, int attempt);

    // Handler failures carry their description, never the exception: its message can quote a recipient or a provider's
    // answer, which is exactly what the stored error leaves out.
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Outbox message {MessageId} ({Type}) failed on attempt {Attempt} ({Error}); retrying in {Delay}.")]
    private static partial void LogRetrying(ILogger logger, Guid messageId, string type, int attempt, string error, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {MessageId} ({Type}) was poisoned at attempt {Attempt}: {Error}")]
    private static partial void LogPoisoned(ILogger logger, Guid messageId, string type, int attempt, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox message {MessageId} ({Type}) was released: the Worker is stopping.")]
    private static partial void LogReleased(ILogger logger, Guid messageId, string type);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Outbox message {MessageId} ({Type}) lost its lease to another delivery, which decides its outcome.")]
    private static partial void LogLeaseLost(ILogger logger, Guid messageId, string type);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The outcome of outbox message {MessageId} ({Type}) was not recorded; it will be delivered again.")]
    private static partial void LogRecordFailed(ILogger logger, Exception exception, Guid messageId, string type);
}
