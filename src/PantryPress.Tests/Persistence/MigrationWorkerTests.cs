using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Persistence;

// PP0.6: failure behavior of the one-shot migration run. The success path needs SQL Server and is covered by
// MigrationServiceTests (Category=Integration).
public sealed class MigrationWorkerTests
{
    private const string Password = "Not-a-real-password-PP06";

    [Fact]
    public void Outcome_is_failure_until_a_run_succeeds()
    {
        Assert.Equal(MigrationOutcome.Failed, new MigrationOutcome().ExitCode);
    }

    [Fact]
    public async Task Non_transient_error_fails_without_retry_stops_the_host_and_never_logs_the_password()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        // Port 1 on loopback refuses connections, which the strategy does not treat as transient.
        using var host = BuildHost(port: 1, maxRetryCount: 5, maxRetryDelay: TimeSpan.FromMilliseconds(50));
        var outcome = host.Services.GetRequiredService<MigrationOutcome>();
        var logs = host.Services.GetFakeLogCollector();

        // Returns only because the worker stopped the host; the timeout is a backstop.
        await host.RunAsync(timeout.Token);

        Assert.False(timeout.IsCancellationRequested);
        Assert.Equal(MigrationOutcome.Failed, outcome.ExitCode);
        Assert.DoesNotContain(logs.GetSnapshot(), r => r.Id == CoreEventId.ExecutionStrategyRetrying);
        Assert.Contains(
            logs.GetSnapshot(),
            r => r.Level == LogLevel.Error && r.Message == "Migration run failed" && r.Exception is not null);
        Assert.All(logs.GetSnapshot(), r =>
        {
            Assert.DoesNotContain(Password, r.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(Password, r.Exception?.ToString() ?? string.Empty, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Transient_errors_are_retried_then_fail_when_retries_run_out()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var endpoint = new ResettingSqlEndpoint();
        using var host = BuildHost(endpoint.Port, maxRetryCount: 2, maxRetryDelay: TimeSpan.FromMilliseconds(50));
        var outcome = host.Services.GetRequiredService<MigrationOutcome>();
        var logs = host.Services.GetFakeLogCollector();

        await host.RunAsync(timeout.Token);

        Assert.False(timeout.IsCancellationRequested);
        Assert.Equal(MigrationOutcome.Failed, outcome.ExitCode);
        Assert.Equal(2, logs.GetSnapshot().Count(r => r.Id == CoreEventId.ExecutionStrategyRetrying));
        Assert.Contains(
            logs.GetSnapshot(),
            r => r.Message == "Migration run failed" && r.Exception is RetryLimitExceededException);
    }

    [Fact]
    public async Task Stopping_mid_run_exits_with_failure_and_logs_a_cancellation_not_an_error()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var endpoint = new ResettingSqlEndpoint();
        using var host = BuildHost(endpoint.Port, maxRetryCount: 50, maxRetryDelay: TimeSpan.FromSeconds(10));
        var outcome = host.Services.GetRequiredService<MigrationOutcome>();
        var logs = host.Services.GetFakeLogCollector();

        await host.StartAsync(timeout.Token);
        while (!logs.GetSnapshot().Any(r => r.Id == CoreEventId.ExecutionStrategyRetrying))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }

        await host.StopAsync(timeout.Token);

        Assert.Equal(MigrationOutcome.Failed, outcome.ExitCode);
        Assert.Contains(
            logs.GetSnapshot(),
            r => r.Level == LogLevel.Warning && r.Message == "Migration run was stopped before it finished");
        Assert.DoesNotContain(logs.GetSnapshot(), r => r.Message == "Migration run failed");
    }

    private static IHost BuildHost(int port, int maxRetryCount, TimeSpan maxRetryDelay)
    {
        var connectionString =
            $"Server=tcp:127.0.0.1,{port};Database=pantrypress;User ID=sa;Password={Password};Encrypt=False;Connect Timeout=2";

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddFakeLogging();
        builder.Services.AddAccountsIdentitySchema();
        builder.Services.AddPantryPressDbContext((_, options) => options.UseSqlServer(
            connectionString,
            sql => sql.EnableRetryOnFailure(maxRetryCount, maxRetryDelay, null)));
        builder.Services.AddMigrationWorker();
        return builder.Build();
    }
}
