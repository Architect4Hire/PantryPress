using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Platform.Contracts;

namespace PantryPress.MigrationService;

/// <summary>
/// Applies pending migrations once through the execution strategy, then applies the operator's PlatformAdmin list
/// (ACCT-005), records the outcome, and stops the host. Transient SQL errors are retried by the strategy; anything
/// else fails the run without a retry, which leaves dependents unstarted and makes the operator look.
/// </summary>
internal sealed partial class MigrationWorker(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    MigrationOutcome outcome,
    ILogger<MigrationWorker> logger) : BackgroundService
{
    // Same name as the application, which ServiceDefaults already registers as a tracing source.
    public const string ActivitySourceName = "PantryPress.MigrationService";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = ActivitySource.StartActivity("Migrating database");

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();

            // The history count before the first successful read, so a retry after a partly applied run still
            // reports every migration this run applied.
            int? appliedBefore = null;
            var strategy = db.Database.CreateExecutionStrategy();
            var applied = await strategy.ExecuteAsync(
                async cancellationToken =>
                {
                    appliedBefore ??= (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).Count();
#pragma warning disable RS0030 // The one place PantryPress applies migrations (OPS-003).
                    await db.Database.MigrateAsync(cancellationToken);
#pragma warning restore RS0030
                    return (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).Count() - appliedBefore.Value;
                },
                stoppingToken);

            LogUpToDate(logger, applied);

            // After the schema, because the role row the list needs is seeded by a migration. A failure here fails the
            // run: the list is a security control, and migrations are idempotent, so a retry is safe.
            // The reconciler logs what it applied; this job only needs the run to succeed.
            await scope.ServiceProvider.GetRequiredService<IPlatformAdminReconciler>().RunAsync(stoppingToken);

            outcome.MarkSucceeded();
        }
        // SqlClient often reports a cancelled command as SqlException, so judge cancellation by the token.
        catch (Exception) when (stoppingToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
            LogCancelled(logger);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            LogFailed(logger, ex);
        }
        finally
        {
            lifetime.StopApplication();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database is up to date; {AppliedCount} migrations applied")]
    private static partial void LogUpToDate(ILogger logger, int appliedCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Migration run was stopped before it finished")]
    private static partial void LogCancelled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Migration run failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
