using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PantryPress.Domain.Managers.Idempotency;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Persistence;

// PP1.9: the store against the schema the migration service applies. The primary key and the conditional updates on
// the lease token are what make a claim exclusive, so these race them for real against SQL Server.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class IdempotencyStoreTests(MigratedDatabase database) : IAsyncLifetime
{
    private static readonly byte[] Fingerprint = Enumerable.Repeat((byte)7, 32).ToArray();
    private readonly string _caller = $"account:{Guid.NewGuid():D}";
    private ServiceProvider _services = null!;

    private FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));

    private IIdempotencyStore Store => _services.GetRequiredService<IIdempotencyStore>();

    public async Task InitializeAsync()
    {
        var connectionString = await database.App.GetConnectionStringAsync(MigrationServiceExtensions.ConnectionName);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountsIdentitySchema();
        services.AddPantryPressDbContext((_, options) => options.UseSqlServer(
            connectionString,
            MigrationServiceExtensions.UseMigrationsAssembly));
        services.AddSingleton<TimeProvider>(Time);
        services.AddApplicationClock();
        services.AddIdempotency(new ConfigurationBuilder().Build());
        _services = services.BuildServiceProvider();
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task Of_many_concurrent_first_claims_exactly_one_acquires_the_key()
    {
        var request = Request();

        var claims = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => Store.ClaimAsync(request, CancellationToken.None))));

        Assert.Single(claims, c => c is IdempotencyClaim.Acquired);
        Assert.All(claims, c => Assert.True(c is IdempotencyClaim.Acquired or IdempotencyClaim.InProgress));
    }

    [Fact]
    public async Task Of_many_concurrent_takeovers_of_an_abandoned_claim_exactly_one_wins()
    {
        var request = Request();
        Assert.IsType<IdempotencyClaim.Acquired>(await Store.ClaimAsync(request, CancellationToken.None));
        Time.Advance(TimeSpan.FromMinutes(5));

        var claims = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => Store.ClaimAsync(request, CancellationToken.None))));

        Assert.Single(claims, c => c is IdempotencyClaim.Acquired);
    }

    [Fact]
    public async Task A_completed_claim_replays_exactly_what_was_stored()
    {
        var request = Request();
        var lease = Assert.IsType<IdempotencyClaim.Acquired>(await Store.ClaimAsync(request, CancellationToken.None)).Lease;
        var headers = new Dictionary<string, string> { ["Content-Type"] = "application/json", ["Location"] = "/x/1" };

        await Store.CompleteAsync(lease, new StoredResponse(201, headers, [1, 2, 3]), CancellationToken.None);
        var replay = Assert.IsType<IdempotencyClaim.Completed>(await Store.ClaimAsync(request, CancellationToken.None));

        Assert.Equal(201, replay.Response.StatusCode);
        Assert.Equal(headers, replay.Response.Headers);
        Assert.Equal([1, 2, 3], replay.Response.Body);
    }

    [Fact]
    public async Task A_lease_that_was_taken_over_can_neither_complete_nor_release_the_new_claim()
    {
        var request = Request();
        var stale = Assert.IsType<IdempotencyClaim.Acquired>(await Store.ClaimAsync(request, CancellationToken.None)).Lease;
        Time.Advance(TimeSpan.FromMinutes(5));
        var current = Assert.IsType<IdempotencyClaim.Acquired>(await Store.ClaimAsync(request, CancellationToken.None)).Lease;

        await Store.ReleaseAsync(stale, CancellationToken.None);
        await Store.CompleteAsync(stale, new StoredResponse(200, new Dictionary<string, string>(), [9]), CancellationToken.None);

        Assert.IsType<IdempotencyClaim.InProgress>(await Store.ClaimAsync(request, CancellationToken.None));
        await Store.CompleteAsync(current, new StoredResponse(201, new Dictionary<string, string>(), [1]), CancellationToken.None);
        var replay = Assert.IsType<IdempotencyClaim.Completed>(await Store.ClaimAsync(request, CancellationToken.None));
        Assert.Equal(201, replay.Response.StatusCode);
    }

    [Fact]
    public async Task A_released_key_is_claimed_again_by_the_next_request()
    {
        var request = Request();
        var lease = Assert.IsType<IdempotencyClaim.Acquired>(await Store.ClaimAsync(request, CancellationToken.None)).Lease;

        await Store.ReleaseAsync(lease, CancellationToken.None);

        Assert.IsType<IdempotencyClaim.Acquired>(await Store.ClaimAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task The_purge_removes_only_records_past_their_retention()
    {
        var old = Request();
        var oldLease = Assert.IsType<IdempotencyClaim.Acquired>(await Store.ClaimAsync(old, CancellationToken.None)).Lease;
        await Store.CompleteAsync(oldLease, new StoredResponse(204, new Dictionary<string, string>(), []), CancellationToken.None);
        Time.Advance(TimeSpan.FromHours(23));
        var recent = Request();
        Assert.IsType<IdempotencyClaim.Acquired>(await Store.ClaimAsync(recent, CancellationToken.None));
        Time.Advance(TimeSpan.FromHours(1));

        // Other tests share the database, so the purge runs until nothing expired is left rather than counting.
        while (await Store.PurgeExpiredAsync(500, CancellationToken.None) > 0)
        {
        }

        await using var scope = _services.CreateAsyncScope();
        var keys = await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<IdempotencyRecord>()
            .Where(r => r.CallerScope == _caller)
            .Select(r => r.Key)
            .ToListAsync();
        Assert.Equal([recent.Key], keys);
    }

    private IdempotencyRequest Request() => new(_caller, $"key-{Guid.NewGuid():N}", Fingerprint);
}
