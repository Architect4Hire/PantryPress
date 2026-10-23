using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PantryPress.Domain.Managers.Time;

public static class TimeServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IClock"/>, <see cref="ITimeZoneService"/>, and the system <see cref="TimeProvider"/>.
    /// Registrations are TryAdd, so a test can register a fake <see cref="TimeProvider"/> first.
    /// </summary>
    public static IServiceCollection AddApplicationClock(this IServiceCollection services)
    {
#pragma warning disable RS0030 // The one place the real system time enters PantryPress; everything else injects it.
        services.TryAddSingleton(TimeProvider.System);
#pragma warning restore RS0030
        services.TryAddSingleton<IClock, TimeProviderClock>();
        services.TryAddSingleton<ITimeZoneService, TzdbTimeZoneService>();
        return services;
    }
}
