using System.Text.Json;
using System.Text.RegularExpressions;

namespace PantryPress.Domain.Managers.Outbox;

/// <summary>The outbox's rules: what a message may be, how long to wait before retrying it, and when to give up.</summary>
public static partial class OutboxRules
{
    public const int MaxTypeLength = 100;

    /// <summary>64 KiB as <c>nvarchar</c>. A message carries references, not the data itself.</summary>
    public const int MaxPayloadLength = 32_768;

    public const int MaxErrorLength = 200;

    /// <summary>A W3C <c>traceparent</c>: version, trace id, span id, and flags.</summary>
    public const int MaxTraceParentLength = 55;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool IsValidType(string type) => type.Length <= MaxTypeLength && TypePattern().IsMatch(type);

    /// <summary>
    /// Refuses a message type that cannot go in the outbox: a badly formed <see cref="IOutboxMessage.OutboxType"/>, or a
    /// property that names a site. The site travels beside the payload, stamped by the writer, so a handler can never be
    /// tempted to trust one read from the payload instead (tenancy.md rule 7).
    /// </summary>
    internal static void EnsureValidMessageType<TMessage>()
        where TMessage : IOutboxMessage
    {
        if (!IsValidType(TMessage.OutboxType))
        {
            throw new InvalidOperationException(
                $"{typeof(TMessage).Name}.OutboxType must be lowercase dot-separated segments of at most " +
                $"{MaxTypeLength} characters.");
        }

        if (typeof(TMessage).GetProperties().Any(p => p.Name.Equals("SiteId", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"{typeof(TMessage).Name} carries a site id. The site goes beside the message, as Enqueue's siteId.");
        }
    }

    /// <summary>
    /// The delay after <paramref name="attempt"/> failed: <see cref="OutboxOptions.RetryBase"/> doubled for each earlier
    /// failure, capped at <see cref="OutboxOptions.RetryCap"/>, then spread over its upper half by
    /// <paramref name="jitter"/> (0 to 1) so messages that failed together do not all come back together.
    /// </summary>
    public static TimeSpan RetryDelay(int attempt, OutboxOptions options, double jitter)
    {
        var doublings = Math.Clamp(attempt - 1, 0, 30);
        var ceiling = Math.Min(options.RetryBase.Ticks * Math.Pow(2, doublings), options.RetryCap.Ticks);
        return TimeSpan.FromTicks((long)(ceiling * (0.5 + (Math.Clamp(jitter, 0, 1) / 2))));
    }

    /// <summary>Whether a failure of <paramref name="attempt"/> poisons the message rather than retrying it.</summary>
    public static bool IsLastAttempt(int attempt, OutboxOptions options) => attempt >= options.MaxAttempts;

    /// <summary>
    /// Whether a claim is past the last attempt: the attempt before it started and never reported back, because the
    /// process died or the lease ran out mid-delivery. Poisoned without running, so a message that kills the Worker
    /// cannot keep killing it.
    /// </summary>
    public static bool IsPastLastAttempt(int attempt, OutboxOptions options) => attempt > options.MaxAttempts;

    /// <summary>
    /// What is stored about a failure: the exception's type, plus a permanent failure's stated reason. Never the message
    /// text of other exceptions, which can quote a recipient or a provider's answer.
    /// </summary>
    public static string Describe(Exception exception)
    {
        var description = exception is OutboxPermanentFailureException
            ? $"{nameof(OutboxPermanentFailureException)}: {exception.Message}"
            : exception.GetType().Name;
        return description.Length <= MaxErrorLength ? description : description[..MaxErrorLength];
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*(\\.[a-z0-9]+(-[a-z0-9]+)*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex TypePattern();
}
