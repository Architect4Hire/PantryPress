using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PantryPress.Domain.Managers.Caching;

public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// The platform cache (PP2.12). The host supplies the connection to Redis - <c>AddCacheConnection</c> from
    /// ServiceDefaults, over the AppHost's <c>redis</c> reference - and this adds the one adapter allowed to use it.
    /// </summary>
    public static IServiceCollection AddPlatformCache(this IServiceCollection services)
    {
        services.TryAddSingleton<IPlatformCache, RedisPlatformCache>();
        return services;
    }
}
