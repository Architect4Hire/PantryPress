using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Idempotency;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Worker.Outbox;

namespace PantryPress.Tests.Managers.Outbox;

// PP1.10: the outbox's rules, and what a writer and a Worker refuse before any database is involved.
public sealed class OutboxRulesTests
{
    private static readonly OutboxOptions Options = new()
    {
        RetryBase = TimeSpan.FromSeconds(5),
        RetryCap = TimeSpan.FromMinutes(30),
        MaxAttempts = 12,
    };

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(9, 1280)]
    [InlineData(10, 1800)]
    [InlineData(12, 1800)]
    [InlineData(400, 1800)]
    public void The_retry_delay_doubles_to_its_cap_and_is_jittered_over_its_upper_half(int attempt, int ceilingSeconds)
    {
        var ceiling = TimeSpan.FromSeconds(ceilingSeconds);

        Assert.Equal(ceiling / 2, OutboxRules.RetryDelay(attempt, Options, jitter: 0));
        Assert.Equal(ceiling, OutboxRules.RetryDelay(attempt, Options, jitter: 1));
        var middle = OutboxRules.RetryDelay(attempt, Options, jitter: 0.5);
        Assert.InRange(middle, ceiling / 2, ceiling);
    }

    [Fact]
    public void The_last_attempt_poisons_and_a_claim_beyond_it_never_runs()
    {
        Assert.False(OutboxRules.IsLastAttempt(11, Options));
        Assert.True(OutboxRules.IsLastAttempt(12, Options));
        Assert.False(OutboxRules.IsPastLastAttempt(12, Options));
        Assert.True(OutboxRules.IsPastLastAttempt(13, Options));
    }

    [Theory]
    [InlineData("email.send", true)]
    [InlineData("rendering.build-page", true)]
    [InlineData("billing.stripe-event.apply", true)]
    [InlineData("email", false)]
    [InlineData("Email.Send", false)]
    [InlineData("email..send", false)]
    [InlineData("email.send-", false)]
    [InlineData("email.send now", false)]
    [InlineData("", false)]
    public void Type_names_are_lowercase_dot_separated_segments(string type, bool valid) =>
        Assert.Equal(valid, OutboxRules.IsValidType(type));

    [Fact]
    public void A_stored_failure_names_the_exception_type_and_never_its_text()
    {
        var described = OutboxRules.Describe(new InvalidOperationException("cook@example.com was refused"));

        Assert.Equal(nameof(InvalidOperationException), described);
        Assert.Equal(
            "OutboxPermanentFailureException: the post is gone",
            OutboxRules.Describe(new OutboxPermanentFailureException("the post is gone")));
        Assert.Equal(OutboxRules.MaxErrorLength, OutboxRules.Describe(new OutboxPermanentFailureException(new string('x', 500))).Length);
    }

    [Theory]
    [InlineData("BatchSize", "0")]
    [InlineData("BatchSize", "101")]
    [InlineData("MaxParallelism", "0")]
    [InlineData("Lease", "00:00:00")]
    [InlineData("MaxAttempts", "0")]
    [InlineData("RetryCap", "00:00:01")]
    [InlineData("PurgeBatchSize", "5001")]
    [InlineData("SweepInterval", "00:00:00")]
    public void Unworkable_settings_are_refused(string key, string value)
    {
        using var services = Services(new Dictionary<string, string?> { [key] = value });

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<OutboxOptions>>().Value);
    }

    [Fact]
    public void The_defaults_are_workable() =>
        Assert.True(new OutboxOptions().IsValid());

    [Fact]
    public void A_message_is_staged_on_the_requests_own_context_with_its_trace()
    {
        using var services = Services();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var site = new SiteId(Guid.NewGuid());
        scope.ServiceProvider.GetRequiredService<ISiteContextWriter>().Enter(site);
        using var activity = new Activity("writer").SetIdFormat(ActivityIdFormat.W3C).Start();

        var id = scope.ServiceProvider.GetRequiredService<IOutbox>().Enqueue(new RecordedWork("staged"), SiteScope.Current);

        var staged = Assert.Single(db.ChangeTracker.Entries<OutboxMessage>());
        Assert.Equal(EntityState.Added, staged.State);
        Assert.Equal(id, staged.Entity.Id);
        Assert.Equal(RecordedWork.OutboxType, staged.Entity.Type);
        Assert.Equal("""{"name":"staged"}""", staged.Entity.Payload);
        Assert.Equal(site.Value, staged.Entity.SiteId);
        Assert.Equal(OutboxState.Pending, staged.Entity.State);
        Assert.Equal(0, staged.Entity.Attempts);
        Assert.Equal(staged.Entity.CreatedAt, staged.Entity.AvailableAt);
        Assert.Equal(activity.Id, staged.Entity.TraceParent);
    }

    [Fact]
    public void A_writer_bug_is_refused_and_nothing_is_staged()
    {
        using var services = Services();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();

        Assert.Throws<ArgumentException>(() => outbox.Enqueue(Guid.Empty, new RecordedWork("no id"), SiteScope.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => outbox.Enqueue(new RecordedWork("no scope"), (SiteScope)0));
        Assert.Throws<InvalidOperationException>(() => outbox.Enqueue(new RecordedWork("no site resolved"), SiteScope.Current));
        Assert.Throws<InvalidOperationException>(() => outbox.Enqueue(new BadlyNamedWork("bad name"), SiteScope.None));
        Assert.Throws<ArgumentException>(() => outbox.Enqueue(new OversizedWork(new string('x', OutboxRules.MaxPayloadLength)), SiteScope.None));
        Assert.Throws<InvalidOperationException>(() => outbox.Enqueue(new SiteCarryingWork(Guid.NewGuid()), SiteScope.None));
        Assert.Throws<InvalidOperationException>(() => outbox.Enqueue(new SiteTypedWork(new SiteId(Guid.NewGuid())), SiteScope.None));

        Assert.Empty(db.ChangeTracker.Entries<OutboxMessage>());
    }

    [Fact]
    public void A_handler_cannot_be_registered_for_a_badly_named_type_or_one_that_carries_a_site()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddOutboxHandler<BadlyNamedWork, BadlyNamedWorkHandler>());
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddOutboxHandler<SiteCarryingWork, SiteCarryingWorkHandler>());
    }

    [Fact]
    public async Task A_Worker_with_no_handlers_starts_no_delivery_loop()
    {
        using var services = Services();
        var dispatcher = services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<OutboxDispatcher>().Single();

        await dispatcher.StartAsync(CancellationToken.None);

        // It finishes by itself without ever asking the database (there is none here), rather than polling for nothing.
        await dispatcher.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(dispatcher.ExecuteTask.IsCompletedSuccessfully);
        await dispatcher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Two_handlers_for_one_type_name_fail_the_Workers_start()
    {
        using var services = Services(configure: s => s
            .AddOutboxHandler<RecordedWork, RecordedWorkHandler>()
            .AddOutboxHandler<ImpostorWork, ImpostorWorkHandler>());

        var refusal = Assert.Throws<InvalidOperationException>(() => services.GetRequiredService<OutboxProcessor>());
        Assert.Contains(RecordedWork.OutboxType, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Worker_claims_exactly_the_types_it_has_handlers_for()
    {
        using var services = Services(configure: s => s.AddOutboxHandler<RecordedWork, RecordedWorkHandler>());

        Assert.Equal(new[] { RecordedWork.OutboxType }, services.GetRequiredService<OutboxProcessor>().Types);
    }

    [Fact]
    public void The_dispatcher_registers_the_delivery_loop_and_the_sweep()
    {
        using var services = Services();

        var hosted = services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().ToList();
        Assert.Single(hosted.OfType<OutboxDispatcher>());
        Assert.Single(hosted.OfType<OutboxMaintenance>());
    }

    // The Worker's composition with an unopened SQL Server context: staging a message never touches the database.
    private static ServiceProvider Services(
        Dictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddAccountsIdentitySchema();
        services.AddPantryPressDbContext((_, o) => o.UseSqlServer("Server=unused;Database=unused"));
        services.AddApplicationClock();
        services.AddSingleton(new OutboxLedger());
        services.AddIdempotency(new ConfigurationBuilder().Build());
        services.AddOutboxDispatcher(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build());
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private sealed class BadlyNamedWorkHandler : IOutboxHandler<BadlyNamedWork>
    {
        public Task HandleAsync(BadlyNamedWork message, OutboxDelivery delivery, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class SiteCarryingWorkHandler : IOutboxHandler<SiteCarryingWork>
    {
        public Task HandleAsync(SiteCarryingWork message, OutboxDelivery delivery, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
