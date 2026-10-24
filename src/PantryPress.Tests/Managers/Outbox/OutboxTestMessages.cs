using System.Collections.Concurrent;
using PantryPress.Domain.Managers.Outbox;

namespace PantryPress.Tests.Managers.Outbox;

// Message types and a recording handler for the PP1.10 outbox tests. Their type names start with "test.", which no
// production handler uses, so a Worker running beside the tests would never claim them.
internal sealed record RecordedWork(string Name) : IOutboxMessage
{
    public static string OutboxType => "test.recorded-work";
}

internal sealed record UnhandledWork(string Name) : IOutboxMessage
{
    public static string OutboxType => "test.unhandled-work";
}

// Claims the same type name as RecordedWork: registering both is the mistake the processor refuses.
internal sealed record ImpostorWork(string Name) : IOutboxMessage
{
    public static string OutboxType => "test.recorded-work";
}

internal sealed record BadlyNamedWork(string Name) : IOutboxMessage
{
    public static string OutboxType => "Test.Badly Named";
}

// The site belongs beside the payload, never in it.
internal sealed record SiteCarryingWork(Guid SiteId) : IOutboxMessage
{
    public static string OutboxType => "test.site-carrying-work";
}

internal sealed record OversizedWork(string Body) : IOutboxMessage
{
    public static string OutboxType => "test.oversized-work";
}

// What the handler saw, and its effect: applied on the first delivery of a message id only, which is the contract every
// real handler keeps (a redelivery has no second effect).
internal sealed class OutboxLedger
{
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<OutboxDelivery>> _deliveries = new();
    private readonly ConcurrentDictionary<Guid, byte> _applied = new();
    private readonly ConcurrentDictionary<Guid, int> _suppressed = new();

    /// <summary>Runs inside the handler before the effect; throwing from it fails the delivery.</summary>
    public Func<RecordedWork, OutboxDelivery, CancellationToken, Task>? Behavior { get; set; }

    /// <summary>Set when any delivery reaches the handler.</summary>
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public System.Diagnostics.ActivityTraceId? LastTraceId { get; private set; }

    public int TotalDeliveries => _deliveries.Values.Sum(q => q.Count);

    public void Record(OutboxDelivery delivery)
    {
        _deliveries.GetOrAdd(delivery.MessageId, _ => new ConcurrentQueue<OutboxDelivery>()).Enqueue(delivery);
        LastTraceId = System.Diagnostics.Activity.Current?.TraceId;
        Started.TrySetResult();
    }

    public IReadOnlyList<OutboxDelivery> Deliveries(Guid messageId) =>
        _deliveries.TryGetValue(messageId, out var queue) ? queue.ToList() : [];

    /// <summary>Applies the message's effect unless an earlier delivery already did.</summary>
    public void Apply(Guid messageId)
    {
        if (!_applied.TryAdd(messageId, 0))
        {
            _suppressed.AddOrUpdate(messageId, 1, (_, count) => count + 1);
        }
    }

    public bool Applied(Guid messageId) => _applied.ContainsKey(messageId);

    public int Suppressed(Guid messageId) => _suppressed.GetValueOrDefault(messageId);
}

internal sealed class RecordedWorkHandler(OutboxLedger ledger) : IOutboxHandler<RecordedWork>
{
    public async Task HandleAsync(RecordedWork message, OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        ledger.Record(delivery);
        if (ledger.Behavior is { } behavior)
        {
            await behavior(message, delivery, cancellationToken);
        }

        ledger.Apply(delivery.MessageId);
    }
}

internal sealed class ImpostorWorkHandler : IOutboxHandler<ImpostorWork>
{
    public Task HandleAsync(ImpostorWork message, OutboxDelivery delivery, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
