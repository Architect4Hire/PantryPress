using PantryPress.Domain.Managers.Tenancy;
using StackExchange.Redis;

namespace PantryPress.Domain.Managers.Caching;

/// <summary>
/// <see cref="IPlatformCache"/> over Redis: the one file that names it (tenancy.md rule 8; <c>DomainBoundaryTests</c>
/// lists it by name). It takes only a <see cref="PlatformCacheKey"/>, so every key it touches begins <c>platform:</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each entry is one Redis hash holding the value and its version, never two keys: a transaction over two keys fails on a
/// clustered Redis when they hash to different slots, and a hash-tag brace to keep them together is what tenancy.md rule 8
/// refuses. A write is a transaction that holds only while the version is still the one read.
/// </para>
/// <para>
/// Once an entry has a version, the hash keeps it for a day from every write and invalidation - far longer than any
/// lookup runs - and only the value expires with its own lifetime, as a field of the hash (Redis 7.4 or later). Were the
/// value's lifetime the hash's, a short-lived fill would take the version with it, the next invalidation would start the
/// count again, and a read from before both could match it and write its stale answer.
/// </para>
/// </remarks>
internal sealed class RedisPlatformCache(IConnectionMultiplexer redis) : IPlatformCache
{
    private static readonly RedisValue ValueField = "v";
    private static readonly RedisValue VersionField = "g";
    private static readonly TimeSpan VersionLifetime = TimeSpan.FromDays(1);

    public async Task<CacheRead> ReadAsync(PlatformCacheKey key)
    {
        var found = await CallAsync(() => Database.HashGetAsync(KeyOf(key), [ValueField, VersionField]));
        return new CacheRead(found[0].IsNull ? null : found[0].ToString(), found[1].IsNull ? 0 : (long)found[1]);
    }

    public async Task<bool> WriteAsync(PlatformCacheKey key, CacheRead seen, string value, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        var redisKey = KeyOf(key);
        var write = Database.CreateTransaction();
        write.AddCondition(seen.Version == 0
            ? Condition.HashNotExists(redisKey, VersionField)
            : Condition.HashEqual(redisKey, VersionField, seen.Version));
        _ = write.HashSetAsync(redisKey, ValueField, value);
        _ = write.HashFieldExpireAsync(redisKey, [ValueField], lifetime);
        _ = write.KeyExpireAsync(redisKey, seen.Version == 0 ? lifetime : VersionLifetime);
        return await CallAsync(() => write.ExecuteAsync());
    }

    public Task InvalidateAsync(PlatformCacheKey key)
    {
        var redisKey = KeyOf(key);
        var invalidate = Database.CreateTransaction();
        _ = invalidate.HashDeleteAsync(redisKey, ValueField);
        _ = invalidate.HashIncrementAsync(redisKey, VersionField);
        _ = invalidate.KeyExpireAsync(redisKey, VersionLifetime);
        return CallAsync(() => invalidate.ExecuteAsync());
    }

    private IDatabase Database => redis.GetDatabase();

    private static RedisKey KeyOf(PlatformCacheKey key) =>
        key.Value ?? throw new ArgumentException("A default cache key has no value.", nameof(key));

    private static async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception failure) when (failure is RedisException or RedisTimeoutException)
        {
            throw new CacheUnavailableException("The cache could not be reached.", failure);
        }
    }
}
