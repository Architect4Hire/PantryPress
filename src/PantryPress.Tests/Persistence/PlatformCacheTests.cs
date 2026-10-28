using System.Diagnostics;
using Aspire.Hosting.Testing;
using PantryPress.Domain.Managers.Caching;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Tests.Site;
using StackExchange.Redis;

namespace PantryPress.Tests.Persistence;

// PP2.12: the platform cache over the test AppHost's Redis. An entry is filled after a miss, so a fill that started before
// an invalidation must never land after it: the version a write carries is what refuses it. And the cache is never worth
// waiting for: unreachable, every call fails at once with the domain's own exception.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class PlatformCacheTests(MigratedDatabase database) : IAsyncLifetime
{
    private ConnectionMultiplexer _redis = null!;
    private IPlatformCache _cache = null!;

    public async Task InitializeAsync()
    {
        var connection = await database.App.GetConnectionStringAsync("redis")
            ?? throw new InvalidOperationException("The test AppHost has no redis connection string.");
        _redis = await ConnectionMultiplexer.ConnectAsync(connection);
        _cache = new RedisPlatformCache(_redis);
    }

    public async Task DisposeAsync() => await _redis.DisposeAsync();

    private static PlatformCacheKey Fresh() => PlatformCacheKey.For("cache-test", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task A_missing_entry_has_no_value_and_no_version()
    {
        Assert.Equal(new CacheRead(null, 0), await _cache.ReadAsync(Fresh()));
    }

    [Fact]
    public async Task A_write_after_a_miss_is_read_back_under_the_platform_key()
    {
        var key = Fresh();
        var seen = await _cache.ReadAsync(key);

        Assert.True(await _cache.WriteAsync(key, seen, "cook", TimeSpan.FromMinutes(1)));

        Assert.Equal(new CacheRead("cook", 0), await _cache.ReadAsync(key));
        Assert.StartsWith("platform:cache-test:", key.Value, StringComparison.Ordinal);
        Assert.True(await _redis.GetDatabase().KeyExistsAsync(key.Value));
    }

    [Fact]
    public async Task A_write_lives_only_as_long_as_it_was_given()
    {
        var key = Fresh();
        await _cache.WriteAsync(key, await _cache.ReadAsync(key), "cook", TimeSpan.FromSeconds(30));

        var lifetime = await _redis.GetDatabase().KeyTimeToLiveAsync(key.Value);
        Assert.NotNull(lifetime);
        Assert.InRange(lifetime.Value, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task A_fill_that_started_before_an_invalidation_is_refused()
    {
        var key = Fresh();
        var stale = await _cache.ReadAsync(key);

        await _cache.InvalidateAsync(key);

        Assert.False(await _cache.WriteAsync(key, stale, "old answer", TimeSpan.FromMinutes(1)));
        Assert.Equal(new CacheRead(null, 1), await _cache.ReadAsync(key));
    }

    [Fact]
    public async Task A_fill_that_started_before_a_second_invalidation_is_refused_too()
    {
        var key = Fresh();
        await _cache.InvalidateAsync(key);
        var stale = await _cache.ReadAsync(key);
        Assert.True(await _cache.WriteAsync(key, stale, "answer", TimeSpan.FromMinutes(1)));

        await _cache.InvalidateAsync(key);

        Assert.False(await _cache.WriteAsync(key, stale, "old answer", TimeSpan.FromMinutes(1)));
        Assert.Equal(new CacheRead(null, 2), await _cache.ReadAsync(key));
    }

    [Fact]
    public async Task A_fill_after_the_invalidation_is_written()
    {
        var key = Fresh();
        await _cache.WriteAsync(key, await _cache.ReadAsync(key), "old answer", TimeSpan.FromMinutes(1));

        await _cache.InvalidateAsync(key);
        var seen = await _cache.ReadAsync(key);

        Assert.Equal(new CacheRead(null, 1), seen);
        Assert.True(await _cache.WriteAsync(key, seen, "new answer", TimeSpan.FromMinutes(1)));
        Assert.Equal(new CacheRead("new answer", 1), await _cache.ReadAsync(key));
    }

    [Fact]
    public async Task A_short_fill_after_an_invalidation_keeps_the_version_once_its_value_has_gone()
    {
        // The value expires on its own; the version must not go with it, or the next invalidation would count from zero
        // again and a read from before both could match it.
        var key = Fresh();
        var stale = await _cache.ReadAsync(key);
        await _cache.InvalidateAsync(key);
        Assert.True(await _cache.WriteAsync(key, await _cache.ReadAsync(key), "answer", TimeSpan.FromSeconds(1)));

        var lifetime = await _redis.GetDatabase().KeyTimeToLiveAsync(key.Value);
        Assert.True(lifetime > TimeSpan.FromHours(1), $"The entry would live {lifetime}.");

        await Task.Delay(TimeSpan.FromSeconds(1.5));

        Assert.Equal(new CacheRead(null, 1), await _cache.ReadAsync(key));
        Assert.False(await _cache.WriteAsync(key, stale, "old answer", TimeSpan.FromMinutes(1)));
        await _cache.InvalidateAsync(key);
        Assert.Equal(new CacheRead(null, 2), await _cache.ReadAsync(key));
    }

    [Fact]
    public async Task A_fill_never_seen_by_an_invalidation_lives_only_its_own_lifetime()
    {
        var key = Fresh();
        await _cache.WriteAsync(key, await _cache.ReadAsync(key), "answer", TimeSpan.FromSeconds(30));

        var lifetime = await _redis.GetDatabase().KeyTimeToLiveAsync(key.Value);
        Assert.NotNull(lifetime);
        Assert.True(lifetime <= TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task An_invalidation_outlives_the_entry_it_dropped()
    {
        var key = Fresh();
        await _cache.InvalidateAsync(key);

        var lifetime = await _redis.GetDatabase().KeyTimeToLiveAsync(key.Value);
        Assert.NotNull(lifetime);
        Assert.True(lifetime.Value > TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task An_unreachable_cache_fails_every_call_at_once()
    {
        // As the hosts connect (CacheConnection): never queue a command while the connection is down.
        var options = ConfigurationOptions.Parse(SiteFactory.UnreachableCache);
        options.BacklogPolicy = BacklogPolicy.FailFast;
        await using var unreachable = await ConnectionMultiplexer.ConnectAsync(options);
        var cache = new RedisPlatformCache(unreachable);
        var key = Fresh();
        var elapsed = Stopwatch.StartNew();

        await Assert.ThrowsAsync<CacheUnavailableException>(() => cache.ReadAsync(key));
        await Assert.ThrowsAsync<CacheUnavailableException>(() => cache.WriteAsync(key, default, "cook", TimeSpan.FromMinutes(1)));
        await Assert.ThrowsAsync<CacheUnavailableException>(() => cache.InvalidateAsync(key));

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"The calls took {elapsed.Elapsed}.");
    }

    [Fact]
    public async Task A_default_key_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _cache.ReadAsync(default));
    }
}
