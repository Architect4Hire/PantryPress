using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace PantryPress.Tests.TwoSite;

/// <summary>
/// A commit that fails once and is retried, as a transient fault is on every host (PP2.14): <see cref="Configure"/> gives a
/// harness's context the interceptor and a strategy that retries exactly that fault once.
/// </summary>
internal sealed class PlantedCommitFault : DbTransactionInterceptor
{
    /// <summary>How many commits were tried.</summary>
    public int Attempts { get; private set; }

    public void Configure(DbContextOptionsBuilder options)
    {
        new SqlServerDbContextOptionsBuilder(options).ExecutionStrategy(d => new RetryOnce(d));
        options.AddInterceptors(this);
    }

    /// <summary>Fails the first commit before it reaches the server, so its transaction rolls back.</summary>
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default) =>
        ++Attempts == 1 ? throw new Fault() : ValueTask.FromResult(result);

    private sealed class Fault() : Exception("A commit that failed before it reached the server.");

    private sealed class RetryOnce(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is Fault;
    }
}
