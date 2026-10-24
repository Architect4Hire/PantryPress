using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PantryPress.Worker.Outbox;

/// <summary>
/// The outbox's telemetry (PP1.10). Counters are tagged with the message type only - never an id, a site, or anything
/// from a payload - so their cardinality stays that of the handler list. <c>pantrypress.outbox.ready.age</c> is what
/// the OPS-004 backlog alert watches: it grows when no Worker is delivering a type, or none keeps up.
/// </summary>
internal sealed class OutboxMetrics
{
    public const string Name = "PantryPress.Outbox";

    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _delivered;
    private readonly Counter<long> _retried;
    private readonly Counter<long> _poisoned;
    private readonly Counter<long> _released;
    private readonly Histogram<double> _duration;
    private long _readyAgeTicks;

    public OutboxMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);
        _delivered = meter.CreateCounter<long>("pantrypress.outbox.delivered", "{message}", "Messages delivered.");
        _retried = meter.CreateCounter<long>("pantrypress.outbox.retried", "{message}", "Deliveries that failed and will be retried.");
        _poisoned = meter.CreateCounter<long>("pantrypress.outbox.poisoned", "{message}", "Messages given up on.");
        _released = meter.CreateCounter<long>("pantrypress.outbox.released", "{message}", "Messages handed back by a stopping Worker.");
        _duration = meter.CreateHistogram<double>("pantrypress.outbox.handler.duration", "s", "How long a handler ran.");
        meter.CreateObservableGauge(
            "pantrypress.outbox.ready.age",
            () => TimeSpan.FromTicks(Interlocked.Read(ref _readyAgeTicks)).TotalSeconds,
            "s",
            "How long the oldest due message has waited for a delivery.");
    }

    public void Delivered(string type, TimeSpan duration)
    {
        _delivered.Add(1, Tag(type));
        _duration.Record(duration.TotalSeconds, Tag(type));
    }

    public void Retried(string type) => _retried.Add(1, Tag(type));

    public void Poisoned(string type) => _poisoned.Add(1, Tag(type));

    public void Released(string type) => _released.Add(1, Tag(type));

    public void ReadyAge(TimeSpan age) => Interlocked.Exchange(ref _readyAgeTicks, age.Ticks);

    private static KeyValuePair<string, object?> Tag(string type) => new("outbox.type", type);
}
