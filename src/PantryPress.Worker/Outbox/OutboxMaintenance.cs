using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Idempotency;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Worker.Outbox;

/// <summary>
/// The Worker's periodic sweep (PP1.10): samples the outbox backlog for its gauge, purges finished outbox messages past
/// their retention, idempotency keys past theirs (PP1.9 left that here), and sign-outs no session can outlive any more
/// (PP1.12). Every step is a batched, idempotent delete, so several Workers sweeping at once only share the work.
/// </summary>
internal sealed partial class OutboxMaintenance(
    IOutboxStore outbox,
    IIdempotencyStore idempotency,
    IServiceScopeFactory scopes,
    OutboxMetrics metrics,
    TimeProvider time,
    IOptions<OutboxOptions> options,
    ILogger<OutboxMaintenance> logger) : BackgroundService
{
    // Bounds one sweep; whatever is left waits for the next, so a large backlog never holds the database for long.
    private const int MaxBatchesPerSweep = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.SweepInterval, time);
        do
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogSweepFailed(logger, ex);
            }
        }
        while (await NextTickAsync(timer, stoppingToken));
    }

    internal async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        metrics.ReadyAge(await outbox.GetReadyAgeAsync(cancellationToken));
        var messages = await PurgeAsync(batch => outbox.PurgeAsync(batch, cancellationToken));
        var keys = await PurgeAsync(batch => idempotency.PurgeExpiredAsync(batch, cancellationToken));
        var signOuts = await PurgeAsync(async batch =>
        {
            // Each batch in a scope of its own, so no context is held across the sweep.
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IAccountMaintenance>()
                .PurgeExpiredSessionRevocationsAsync(batch, cancellationToken);
        });
        if (messages + keys + signOuts > 0)
        {
            LogPurged(logger, messages, keys, signOuts);
        }
    }

    private async Task<int> PurgeAsync(Func<int, Task<int>> purge)
    {
        var batch = options.Value.PurgeBatchSize;
        var total = 0;
        for (var i = 0; i < MaxBatchesPerSweep; i++)
        {
            var deleted = await purge(batch);
            total += deleted;
            if (deleted < batch)
            {
                break;
            }
        }

        return total;
    }

    private static async Task<bool> NextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Purged {Messages} outbox messages, {Keys} idempotency keys, and {SignOuts} expired sign-outs.")]
    private static partial void LogPurged(ILogger logger, int messages, int keys, int signOuts);

    [LoggerMessage(Level = LogLevel.Error, Message = "The outbox sweep failed; it runs again at the next interval.")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);
}
