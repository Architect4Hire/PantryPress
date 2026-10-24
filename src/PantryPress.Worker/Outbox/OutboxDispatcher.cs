using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Outbox;

namespace PantryPress.Worker.Outbox;

/// <summary>
/// Runs <see cref="OutboxProcessor"/> for as long as the Worker runs (PP1.10): straight on while claims come back full,
/// otherwise after a jittered poll interval, and with growing pauses while the database is unreachable. Nothing it
/// meets stops the host.
/// </summary>
internal sealed partial class OutboxDispatcher(
    OutboxProcessor processor,
    TimeProvider time,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan MaxFailurePause = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (processor.Types.Count == 0)
        {
            LogNoHandlers(logger);
            return;
        }

        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan pause;
            try
            {
                var claimed = await processor.RunOnceAsync(stoppingToken);
                failures = 0;
                if (claimed >= options.Value.BatchSize)
                {
                    continue;
                }

                pause = options.Value.PollInterval * (0.75 + (Random.Shared.NextDouble() / 2));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                failures++;
                LogClaimFailed(logger, ex, failures);
                var backoff = options.Value.PollInterval * Math.Pow(2, Math.Min(failures, 10));
                pause = backoff < MaxFailurePause ? backoff : MaxFailurePause;
            }

            try
            {
                await Task.Delay(pause, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "No outbox handlers are registered; this Worker delivers no messages.")]
    private static partial void LogNoHandlers(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Claiming outbox messages failed ({Failures} in a row).")]
    private static partial void LogClaimFailed(ILogger logger, Exception exception, int failures);
}
