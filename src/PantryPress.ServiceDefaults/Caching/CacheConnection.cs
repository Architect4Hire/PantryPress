using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace PantryPress.ServiceDefaults.Caching;

/// <summary>
/// The connection to PantryPress's own Redis (PP-D16; PP2.12), through the Aspire client: the connection string from the
/// AppHost's <c>redis</c> reference, with its tracing. Nothing here reads or writes the cache; the domain's one adapter does
/// that, by key type (tenancy.md rule 8), and this file is the only other place that names Redis
/// (<c>DomainBoundaryTests</c>).
/// </summary>
public static class CacheConnection
{
    /// <summary>The AppHost's Redis resource.</summary>
    public const string ConnectionName = "redis";

    /// <summary>How long one cache command may take: a hit is a fraction of a millisecond, and a miss reads the database.</summary>
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>How long connecting may take.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    /// <remarks>
    /// Every user of the cache carries on without it - the public tier reads the database instead, and a write's refresh
    /// is bounded by each entry's lifetime - so it is never waited for: a command while the connection is down fails at
    /// once rather than queue, one to a server that has stopped answering gives up after <see cref="CommandTimeout"/>,
    /// and an outage does not make the host unready, which would take a working host out of rotation for want of an
    /// optimization. The first connection is still made when the cache is first used, and waits up to
    /// <see cref="ConnectTimeout"/> if Redis is not there.
    /// </remarks>
    public static TBuilder AddCacheConnection<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.AddRedisClient(
            ConnectionName,
            settings => settings.DisableHealthChecks = true,
            options =>
            {
                options.AbortOnConnectFail = false;
                options.BacklogPolicy = BacklogPolicy.FailFast;
                options.AsyncTimeout = (int)CommandTimeout.TotalMilliseconds;
                options.SyncTimeout = (int)CommandTimeout.TotalMilliseconds;
                options.ConnectTimeout = (int)ConnectTimeout.TotalMilliseconds;
            });
        return builder;
    }
}
