using System.Diagnostics;
using System.Diagnostics.Metrics;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using PantryPress.Domain.Managers.Email;
using PantryPress.Domain.Managers.Idempotency;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.MigrationService;
using PantryPress.Tests.Fakes;
using PantryPress.Tests.Managers.Outbox;
using PantryPress.Worker.Outbox;

namespace PantryPress.Tests.Persistence;

// PP1.10: the transactional outbox against the schema the migration service applies - a message commits with the change
// it was staged with, and the Worker's processor delivers it at least once through crashes, lost leases, failures,
// competing Workers, and a stop, with every duplicate delivery carrying the same message id. Each worker here is a
// provider of its own over the shared database, as each Worker process is, with the production context's retrying
// execution strategy; the clock is fake and moved by the test.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class OutboxTests(MigratedDatabase database) : IAsyncLifetime
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly OutboxLedger _ledger = new();
    private readonly List<ServiceProvider> _workers = [];
    private string _connectionString = string.Empty;

    private FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));

    private ServiceProvider Worker => _workers[0];

    private OutboxProcessor Processor => Worker.GetRequiredService<OutboxProcessor>();

    private IOutboxStore Store => Worker.GetRequiredService<IOutboxStore>();

    private DateTimeOffset Now => Worker.GetRequiredService<IClock>().UtcNow;

    public async Task InitializeAsync()
    {
        _connectionString = await database.App.GetConnectionStringAsync(MigrationServiceExtensions.ConnectionName)
            ?? throw new InvalidOperationException("The test AppHost has no pantrypress connection string.");
        AddWorker();

        // Tests in this collection run one at a time and nothing else writes the outbox, so each starts from an empty one.
        await using var scope = Worker.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<OutboxMessage>().ExecuteDeleteAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var worker in _workers)
        {
            await worker.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_message_commits_with_the_change_it_was_staged_with()
    {
        await using var scope = Worker.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var user = NewUser();
        db.Users.Add(user);
        var id = scope.ServiceProvider.GetRequiredService<IOutbox>().Enqueue(new RecordedWork("with a user"), SiteScope.None);

        await db.SaveChangesAsync();

        Assert.True(await db.Users.AnyAsync(u => u.Id == user.Id));
        Assert.Equal(OutboxState.Pending, (await RowAsync(id)).State);
    }

    [Fact]
    public async Task A_save_that_fails_leaves_neither_the_change_nor_its_message()
    {
        var taken = NewUser();
        await using (var first = Worker.CreateAsyncScope())
        {
            var db = first.ServiceProvider.GetRequiredService<PantryPressDbContext>();
            db.Users.Add(taken);
            await db.SaveChangesAsync();
        }

        await using var scope = Worker.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var duplicate = NewUser();
        duplicate.NormalizedEmail = taken.NormalizedEmail;
        context.Users.Add(duplicate);
        var id = scope.ServiceProvider.GetRequiredService<IOutbox>().Enqueue(new RecordedWork("lost with the save"), SiteScope.None);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.False(await AnyRowAsync(id));
        Assert.False(await context.Users.AsNoTracking().AnyAsync(u => u.Id == duplicate.Id));
        Assert.Equal(0, await Processor.RunOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_rolled_back_transaction_takes_its_message_with_it()
    {
        // Under the retrying execution strategy a facade's transaction runs inside the strategy, as here.
        await using var scope = Worker.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var id = Guid.Empty;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Users.Add(NewUser());
            id = scope.ServiceProvider.GetRequiredService<IOutbox>().Enqueue(new RecordedWork("rolled back"), SiteScope.None);
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        });

        Assert.False(await AnyRowAsync(id));
    }

    [Fact]
    public async Task A_due_message_is_delivered_once_and_its_payload_cleared()
    {
        var id = await EnqueueAsync(new RecordedWork("once"));

        Assert.Equal(1, await Processor.RunOnceAsync(CancellationToken.None));
        Assert.Equal(0, await Processor.RunOnceAsync(CancellationToken.None));

        var delivery = Assert.Single(_ledger.Deliveries(id));
        Assert.Equal(new OutboxDelivery(id, null, 1), delivery);
        var row = await RowAsync(id);
        Assert.Equal(OutboxState.Succeeded, row.State);
        Assert.Null(row.Payload);
        Assert.Null(row.LeaseToken);
        Assert.Equal(Now, row.CompletedAt);
        Assert.Equal(1, row.Attempts);
    }

    [Fact]
    public async Task A_delivery_joins_the_trace_of_the_request_that_wrote_it()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OutboxMetrics.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        ActivityTraceId writer;
        Guid id;
        using (var request = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            writer = request.TraceId;
            id = await EnqueueAsync(new RecordedWork("traced"));
        }

        await Processor.RunOnceAsync(CancellationToken.None);

        Assert.Single(_ledger.Deliveries(id));
        Assert.Equal(writer, _ledger.LastTraceId);
    }

    [Fact]
    public async Task A_delivery_lost_to_a_crash_is_delivered_again_once_its_lease_runs_out()
    {
        var id = await EnqueueAsync(new RecordedWork("crashed"));

        // A Worker claims and starts the message, then dies mid-delivery: it never reports back.
        var crashed = Assert.Single(await Store.ClaimAsync([RecordedWork.OutboxType], 10, CancellationToken.None));
        Assert.Equal(1, (await Store.StartAsync(crashed, CancellationToken.None))?.Attempt);
        Assert.Equal(0, await Processor.RunOnceAsync(CancellationToken.None));

        Time.Advance(Lease);
        Assert.Equal(1, await Processor.RunOnceAsync(CancellationToken.None));

        var delivery = Assert.Single(_ledger.Deliveries(id));
        Assert.Equal(2, delivery.Attempt);
        Assert.Equal(OutboxState.Succeeded, (await RowAsync(id)).State);

        // The crashed Worker coming back to life cannot record anything over the delivery that replaced it.
        Assert.Null(await Store.StartAsync(crashed, CancellationToken.None));
        Assert.False(await Store.CompleteAsync(crashed.Lease, CancellationToken.None));
        Assert.False(await Store.RetryAsync(crashed.Lease, Now, "late", CancellationToken.None));
        Assert.False(await Store.PoisonAsync(crashed.Lease, "late", CancellationToken.None));
        Assert.False(await Store.ReleaseAsync(crashed.Lease, CancellationToken.None));
        var row = await RowAsync(id);
        Assert.Equal(OutboxState.Succeeded, row.State);
        Assert.Equal(2, row.Attempts);
        Assert.Null(row.LastError);
    }

    [Fact]
    public async Task A_claim_taken_over_before_it_started_is_never_run_and_costs_nothing()
    {
        var id = await EnqueueAsync(new RecordedWork("waited too long"));

        // A Worker claims the message, and it waits in that Worker's batch until the claim's lease runs out.
        var stale = Assert.Single(await Store.ClaimAsync([RecordedWork.OutboxType], 10, CancellationToken.None));
        Time.Advance(Lease);
        Assert.Equal(1, await Processor.RunOnceAsync(CancellationToken.None));

        Assert.Null(await Store.StartAsync(stale, CancellationToken.None));
        Assert.Single(_ledger.Deliveries(id));
        Assert.Equal(1, (await RowAsync(id)).Attempts);
    }

    [Fact]
    public async Task Waiting_in_the_batch_behind_a_slow_delivery_costs_a_message_none_of_its_lease()
    {
        // One delivery at a time: the second message waits most of a lease behind the first.
        var worker = AddWorker(new Dictionary<string, string?> { ["MaxParallelism"] = "1" });
        var firstMayFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondMayFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ledger.Behavior = (message, _, ct) =>
        {
            if (message.Name == "first")
            {
                return firstMayFinish.Task.WaitAsync(ct);
            }

            secondStarted.TrySetResult();
            return secondMayFinish.Task.WaitAsync(ct);
        };
        var first = await EnqueueAsync(new RecordedWork("first"));
        Time.Advance(TimeSpan.FromTicks(1));
        var second = await EnqueueAsync(new RecordedWork("second"));

        var run = worker.GetRequiredService<OutboxProcessor>().RunOnceAsync(CancellationToken.None);
        await _ledger.Started.Task.WaitAsync(Wait);
        Time.Advance(Lease * 0.85);
        firstMayFinish.SetResult();
        await secondStarted.Task.WaitAsync(Wait);

        // The second message's lease began when its delivery did, not when the batch was claimed.
        var started = await RowAsync(second);
        Assert.Equal(Now + Lease, started.AvailableAt);
        Assert.Equal(1, started.Attempts);

        secondMayFinish.SetResult();
        Assert.Equal(2, await run.WaitAsync(Wait));
        Assert.Equal(OutboxState.Succeeded, (await RowAsync(first)).State);
        Assert.Equal(OutboxState.Succeeded, (await RowAsync(second)).State);
        Assert.Equal(1, (await RowAsync(second)).Attempts);
    }

    [Fact]
    public async Task A_message_that_kills_the_Worker_every_time_is_poisoned_without_running_again()
    {
        var id = await EnqueueAsync(new RecordedWork("poison pill"));
        for (var i = 0; i < MaxAttempts; i++)
        {
            var claimed = Assert.Single(await Store.ClaimAsync([RecordedWork.OutboxType], 10, CancellationToken.None));
            Assert.NotNull(await Store.StartAsync(claimed, CancellationToken.None));
            Time.Advance(Lease);
        }

        Assert.Equal(1, await Processor.RunOnceAsync(CancellationToken.None));

        Assert.Empty(_ledger.Deliveries(id));
        var row = await RowAsync(id);
        Assert.Equal(OutboxState.Poisoned, row.State);
        Assert.Equal("The last attempt never finished.", row.LastError);
        Assert.NotNull(row.Payload);
    }

    [Fact]
    public async Task A_handler_that_outlives_its_lease_is_cancelled_before_the_lease_ends_and_retried()
    {
        _ledger.Behavior = (_, _, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct);
        var id = await EnqueueAsync(new RecordedWork("slow"));

        var run = Processor.RunOnceAsync(CancellationToken.None);
        await _ledger.Started.Task.WaitAsync(Wait);
        Time.Advance(Lease * 0.85);
        Assert.False(run.IsCompleted);
        Time.Advance(Lease * 0.05);
        await run.WaitAsync(Wait);

        var row = await RowAsync(id);
        Assert.Equal(OutboxState.Pending, row.State);
        Assert.Equal(1, row.Attempts);
        Assert.Null(row.LeaseToken);
        Assert.Equal(nameof(TaskCanceledException), row.LastError);
        Assert.False(_ledger.Applied(id));
    }

    [Fact]
    public async Task A_failed_delivery_is_retried_after_a_backoff_and_then_delivered()
    {
        _ledger.Behavior = (_, delivery, _) => delivery.Attempt == 1
            ? throw new InvalidOperationException("the provider is down")
            : Task.CompletedTask;
        var id = await EnqueueAsync(new RecordedWork("flaky"));

        await Processor.RunOnceAsync(CancellationToken.None);

        var failed = await RowAsync(id);
        Assert.Equal(OutboxState.Pending, failed.State);
        Assert.Equal(nameof(InvalidOperationException), failed.LastError);
        Assert.InRange(failed.AvailableAt - Now, TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(5));
        Assert.Equal(0, await Processor.RunOnceAsync(CancellationToken.None));

        Time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, await Processor.RunOnceAsync(CancellationToken.None));

        Assert.Equal(new[] { 1, 2 }, _ledger.Deliveries(id).Select(d => d.Attempt));
        var delivered = await RowAsync(id);
        Assert.Equal(OutboxState.Succeeded, delivered.State);
        Assert.Null(delivered.LastError);
    }

    [Fact]
    public async Task A_message_that_keeps_failing_is_poisoned_at_its_last_attempt_and_its_payload_kept()
    {
        using var poisoned = new MetricCollector<long>(
            Worker.GetRequiredService<IMeterFactory>(),
            OutboxMetrics.Name,
            "pantrypress.outbox.poisoned");
        _ledger.Behavior = (_, _, _) => throw new InvalidOperationException("cook@example.com could not be reached");
        var id = await EnqueueAsync(new RecordedWork("hopeless"));

        for (var i = 0; i < MaxAttempts; i++)
        {
            Assert.Equal(1, await Processor.RunOnceAsync(CancellationToken.None));
            Time.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(0, await Processor.RunOnceAsync(CancellationToken.None));
        Assert.Equal(MaxAttempts, _ledger.Deliveries(id).Count);
        var row = await RowAsync(id);
        Assert.Equal(OutboxState.Poisoned, row.State);
        Assert.Equal(MaxAttempts, row.Attempts);
        Assert.Equal("""{"name":"hopeless"}""", row.Payload);
        var measurement = Assert.Single(poisoned.GetMeasurementSnapshot());
        Assert.Equal(RecordedWork.OutboxType, measurement.Tags["outbox.type"]);

        // The failures were logged - by type, never by the exception's text, which named a person.
        var logs = Worker.GetFakeLogCollector().GetSnapshot();
        Assert.Contains(logs, r => r.Message.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
        Assert.DoesNotContain(
            logs,
            r => r.Message.Contains("cook@example.com", StringComparison.Ordinal)
                || (r.Exception?.ToString().Contains("cook@example.com", StringComparison.Ordinal) ?? false));
    }

    [Fact]
    public async Task A_permanent_failure_poisons_at_once()
    {
        _ledger.Behavior = (_, _, _) => throw new OutboxPermanentFailureException("the post is gone");
        var id = await EnqueueAsync(new RecordedWork("gone"));

        await Processor.RunOnceAsync(CancellationToken.None);

        var row = await RowAsync(id);
        Assert.Equal(OutboxState.Poisoned, row.State);
        Assert.Equal(1, row.Attempts);
        Assert.Equal("OutboxPermanentFailureException: the post is gone", row.LastError);
    }

    [Fact]
    public async Task A_payload_that_does_not_read_as_its_type_is_poisoned_without_reaching_the_handler()
    {
        var id = await InsertRawAsync("not json");

        await Processor.RunOnceAsync(CancellationToken.None);

        Assert.Empty(_ledger.Deliveries(id));
        var row = await RowAsync(id);
        Assert.Equal(OutboxState.Poisoned, row.State);
        Assert.StartsWith(nameof(OutboxPermanentFailureException), row.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_row_with_no_payload_is_poisoned_rather_than_stalling_the_queue_behind_it()
    {
        var broken = await InsertRawAsync(payload: null);
        Time.Advance(TimeSpan.FromTicks(1));
        var healthy = await EnqueueAsync(new RecordedWork("behind a broken row"));

        Assert.Equal(2, await Processor.RunOnceAsync(CancellationToken.None));

        var row = await RowAsync(broken);
        Assert.Equal(OutboxState.Poisoned, row.State);
        Assert.Equal("The message has no payload.", row.LastError);
        Assert.Single(_ledger.Deliveries(healthy));
    }

    [Fact]
    public async Task Competing_Workers_deliver_each_message_exactly_once()
    {
        var ids = await EnqueueManyAsync(200);
        var workers = new[] { Processor, AddWorker().GetRequiredService<OutboxProcessor>(), AddWorker().GetRequiredService<OutboxProcessor>() };

        await DrainAsync(workers);

        Assert.Equal(200, _ledger.TotalDeliveries);
        Assert.All(ids, id => Assert.Single(_ledger.Deliveries(id)));
        Assert.All(ids, id => Assert.True(_ledger.Applied(id)));
        Assert.Equal(200, await CountAsync(OutboxState.Succeeded));
    }

    [Fact]
    public async Task A_redelivery_carries_the_same_message_id_so_competing_Workers_never_repeat_an_effect()
    {
        var ids = await EnqueueManyAsync(60);

        // A Worker claims everything, starts each delivery, applies each effect, and dies before recording any of it.
        var lost = await Store.ClaimAsync([RecordedWork.OutboxType], 100, CancellationToken.None);
        Assert.Equal(60, lost.Count);
        foreach (var envelope in lost)
        {
            Assert.NotNull(await Store.StartAsync(envelope, CancellationToken.None));
            _ledger.Apply(envelope.Lease.MessageId);
        }

        Time.Advance(Lease);
        await DrainAsync([Processor, AddWorker().GetRequiredService<OutboxProcessor>(), AddWorker().GetRequiredService<OutboxProcessor>()]);

        Assert.All(ids, id =>
        {
            var delivery = Assert.Single(_ledger.Deliveries(id));
            Assert.Equal(id, delivery.MessageId);
            Assert.Equal(2, delivery.Attempt);
            Assert.Equal(1, _ledger.Suppressed(id));
        });
        Assert.Equal(60, await CountAsync(OutboxState.Succeeded));
    }

    [Fact]
    public async Task A_stopping_Worker_hands_back_what_it_claimed_charging_only_the_delivery_it_cut_short()
    {
        // One delivery at a time: the first blocks until the stop, and the other two are claimed but never started.
        var worker = AddWorker(new Dictionary<string, string?> { ["MaxParallelism"] = "1" });
        _ledger.Behavior = (_, _, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct);
        var ids = await EnqueueManyAsync(3);
        using var stopping = new CancellationTokenSource();

        var run = worker.GetRequiredService<OutboxProcessor>().RunOnceAsync(stopping.Token);
        await _ledger.Started.Task.WaitAsync(Wait);
        await stopping.CancelAsync();
        Assert.Equal(3, await run.WaitAsync(Wait));

        Assert.Equal(1, _ledger.TotalDeliveries);
        foreach (var id in ids)
        {
            var row = await RowAsync(id);
            Assert.Equal(OutboxState.Pending, row.State);
            Assert.Equal(_ledger.Deliveries(id).Count, row.Attempts);
            Assert.Null(row.LeaseToken);
            Assert.Equal(Now, row.AvailableAt);
        }
    }

    [Fact]
    public async Task The_hosted_dispatcher_delivers_and_stops_cleanly()
    {
        var id = await EnqueueAsync(new RecordedWork("hosted"));
        var dispatcher = Worker.GetServices<IHostedService>().OfType<OutboxDispatcher>().Single();

        await dispatcher.StartAsync(CancellationToken.None);
        await _ledger.Started.Task.WaitAsync(Wait);
        var deadline = Stopwatch.StartNew();
        while ((await RowAsync(id)).State != OutboxState.Succeeded && deadline.Elapsed < Wait)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        await dispatcher.StopAsync(CancellationToken.None).WaitAsync(Wait);

        Assert.Equal(OutboxState.Succeeded, (await RowAsync(id)).State);
        Assert.True(dispatcher.ExecuteTask?.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_Worker_never_claims_a_type_it_has_no_handler_for_and_the_backlog_shows_it()
    {
        var unhandled = await EnqueueAsync(new UnhandledWork("for a newer Worker"));
        var handled = await EnqueueAsync(new RecordedWork("mine"));
        using var readyAge = new MetricCollector<double>(
            Worker.GetRequiredService<IMeterFactory>(),
            OutboxMetrics.Name,
            "pantrypress.outbox.ready.age");

        Assert.Equal(1, await Processor.RunOnceAsync(CancellationToken.None));
        Time.Advance(TimeSpan.FromMinutes(10));
        await Sweep().SweepOnceAsync(CancellationToken.None);
        readyAge.RecordObservableInstruments();

        Assert.Single(_ledger.Deliveries(handled));
        var row = await RowAsync(unhandled);
        Assert.Equal(OutboxState.Pending, row.State);
        Assert.Equal(0, row.Attempts);
        Assert.Equal(TimeSpan.FromMinutes(10).TotalSeconds, readyAge.LastMeasurement?.Value);
    }

    [Fact]
    public async Task Two_sites_messages_stay_apart_each_delivered_with_its_own_site_and_failing_alone()
    {
        var siteA = Guid.NewGuid();
        var siteB = Guid.NewGuid();
        _ledger.Behavior = (_, delivery, _) => delivery.SiteId?.Value == siteA
            ? throw new OutboxPermanentFailureException("site A's target is gone")
            : Task.CompletedTask;

        // The same payload for site A, site B, and the platform: three messages, never one.
        var forA = await EnqueueAsync(new RecordedWork("lemon-tart"), siteA);
        var forB = await EnqueueAsync(new RecordedWork("lemon-tart"), siteB);
        var forPlatform = await EnqueueAsync(new RecordedWork("lemon-tart"));

        await Processor.RunOnceAsync(CancellationToken.None);

        Assert.Equal(new SiteId(siteA), Assert.Single(_ledger.Deliveries(forA)).SiteId);
        Assert.Equal(new SiteId(siteB), Assert.Single(_ledger.Deliveries(forB)).SiteId);
        Assert.Null(Assert.Single(_ledger.Deliveries(forPlatform)).SiteId);
        Assert.Equal(OutboxState.Poisoned, (await RowAsync(forA)).State);
        Assert.Equal(OutboxState.Succeeded, (await RowAsync(forB)).State);
        Assert.Equal(OutboxState.Succeeded, (await RowAsync(forPlatform)).State);
        Assert.False(_ledger.Applied(forA));
        Assert.True(_ledger.Applied(forB));
        Assert.Equal(siteB, (await RowAsync(forB)).SiteId);
    }

    [Fact]
    public async Task The_sweep_purges_what_is_past_its_retention_and_keeps_the_rest()
    {
        _ledger.Behavior = (message, _, _) => message.Name == "poisoned"
            ? throw new OutboxPermanentFailureException("kept for an operator")
            : Task.CompletedTask;
        var succeeded = await EnqueueAsync(new RecordedWork("succeeded"));
        var poisoned = await EnqueueAsync(new RecordedWork("poisoned"));
        var pending = await EnqueueAsync(new UnhandledWork("pending"));
        await Processor.RunOnceAsync(CancellationToken.None);
        var caller = $"account:{Guid.NewGuid():D}";
        Assert.IsType<IdempotencyClaim.Acquired>(await Worker.GetRequiredService<IIdempotencyStore>().ClaimAsync(
            new IdempotencyRequest(caller, "key", Enumerable.Repeat((byte)1, 32).ToArray()),
            CancellationToken.None));

        Time.Advance(TimeSpan.FromDays(7));
        await Sweep().SweepOnceAsync(CancellationToken.None);

        Assert.False(await AnyRowAsync(succeeded));
        Assert.True(await AnyRowAsync(poisoned));
        Assert.True(await AnyRowAsync(pending));
        Assert.False(await AnyKeyAsync(caller));

        Time.Advance(TimeSpan.FromDays(23));
        await Sweep().SweepOnceAsync(CancellationToken.None);

        Assert.False(await AnyRowAsync(poisoned));
        Assert.True(await AnyRowAsync(pending));
    }

    [Fact]
    public async Task The_sweep_forgets_a_sign_out_once_no_session_can_outlive_it()
    {
        // PP1.12: a signed-out session's id is remembered for SessionLimits.RevocationRetention, longer than the
        // gateway's absolute session lifetime, and then deleted.
        var account = Guid.NewGuid();
        var signedOut = $"signed-out-{Guid.NewGuid():N}";
        var stillLive = $"still-live-{Guid.NewGuid():N}";
        await using (var scope = Worker.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
            db.Set<RevokedSession>().Add(new RevokedSession
            {
                SessionId = signedOut,
                AccountId = account,
                RevokedAt = Now,
                ExpiresAt = Now + SessionLimits.RevocationRetention,
            });
            db.Set<RevokedSession>().Add(new RevokedSession
            {
                SessionId = stillLive,
                AccountId = account,
                RevokedAt = Now,
                ExpiresAt = Now + SessionLimits.RevocationRetention + TimeSpan.FromHours(1),
            });
            await db.SaveChangesAsync();
        }

        Time.Advance(SessionLimits.RevocationRetention);
        await Sweep().SweepOnceAsync(CancellationToken.None);

        await using var check = Worker.CreateAsyncScope();
        var remaining = await check.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<RevokedSession>()
            .AsNoTracking()
            .Where(r => r.AccountId == account)
            .Select(r => r.SessionId)
            .ToListAsync();
        Assert.Equal([stillLive], remaining);
    }

    // A Worker of its own over the shared database: the real dispatcher composition and the production context's
    // retrying execution strategy, with the hosted services not started unless a test starts one.
    private ServiceProvider AddWorker(Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Lease"] = Lease.ToString(),
            ["MaxAttempts"] = MaxAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RetryBase"] = "00:00:05",
            ["RetryCap"] = "00:01:00",
            ["BatchSize"] = "16",
            ["MaxParallelism"] = "4",
        };
        foreach (var (key, value) in overrides ?? [])
        {
            settings[key] = value;
        }

        var services = new ServiceCollection();
        services.AddFakeLogging();
        services.AddMetrics();
        services.AddAccountsIdentitySchema();
        services.AddPantryPressDbContext((_, options) => options.UseSqlServer(_connectionString, sql =>
        {
            MigrationServiceExtensions.UseMigrationsAssembly(sql);
            sql.EnableRetryOnFailure();
        }));
        services.AddSingleton<TimeProvider>(Time);
        services.AddApplicationClock();
        services.AddIdempotency(new ConfigurationBuilder().Build());

        // Accounts' share of the Worker, as Program registers it: the sweep also forgets expired sign-outs (PP1.12).
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<IEmailGateway>(new RecordingEmailGateway());
        services.AddAccountsWorker(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AppBaseUrl"] = "https://app.localhost" })
                .Build(),
            sendsEmail: true);
        services.AddSingleton(_ledger);
        services.AddOutboxDispatcher(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddOutboxHandler<RecordedWork, RecordedWorkHandler>();
        var provider = services.BuildServiceProvider();
        _workers.Add(provider);
        return provider;
    }

    private static Task DrainAsync(IEnumerable<OutboxProcessor> workers) =>
        Task.WhenAll(workers.Select(worker => Task.Run(async () =>
        {
            while (await worker.RunOnceAsync(CancellationToken.None) > 0)
            {
            }
        })));

    private OutboxMaintenance Sweep() => Worker.GetServices<IHostedService>().OfType<OutboxMaintenance>().Single();

    private async Task<Guid> EnqueueAsync<TMessage>(TMessage message, Guid? siteId = null)
        where TMessage : IOutboxMessage
    {
        await using var scope = Worker.CreateAsyncScope();
        if (siteId is { } site)
        {
            scope.ServiceProvider.GetRequiredService<ISiteContextWriter>().Enter(new SiteId(site));
        }

        var id = scope.ServiceProvider.GetRequiredService<IOutbox>().Enqueue(message, siteId is null ? SiteScope.None : SiteScope.Current);
        await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().SaveChangesAsync();
        return id;
    }

    private async Task<List<Guid>> EnqueueManyAsync(int count)
    {
        await using var scope = Worker.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
        var ids = Enumerable.Range(0, count).Select(i => outbox.Enqueue(new RecordedWork($"work {i}"), SiteScope.None)).ToList();
        await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().SaveChangesAsync();
        return ids;
    }

    // A row no writer could produce, as a bug or a hand edit might leave one.
    private async Task<Guid> InsertRawAsync(string? payload)
    {
        var id = Guid.NewGuid();
        await using var scope = Worker.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = id,
            Type = RecordedWork.OutboxType,
            Payload = payload,
            State = OutboxState.Pending,
            CreatedAt = Now,
            AvailableAt = Now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<OutboxMessage> RowAsync(Guid id)
    {
        await using var scope = Worker.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<OutboxMessage>()
            .AsNoTracking()
            .SingleAsync(m => m.Id == id);
    }

    private async Task<bool> AnyRowAsync(Guid id)
    {
        await using var scope = Worker.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<OutboxMessage>()
            .AnyAsync(m => m.Id == id);
    }

    private async Task<int> CountAsync(OutboxState state)
    {
        await using var scope = Worker.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<OutboxMessage>()
            .CountAsync(m => m.State == state);
    }

    private async Task<bool> AnyKeyAsync(string caller)
    {
        await using var scope = Worker.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<IdempotencyRecord>()
            .AnyAsync(r => r.CallerScope == caller);
    }

    private static ApplicationUser NewUser()
    {
        var id = Guid.NewGuid();
        var email = $"outbox-{id:N}@example.com";
        return new ApplicationUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        };
    }
}
