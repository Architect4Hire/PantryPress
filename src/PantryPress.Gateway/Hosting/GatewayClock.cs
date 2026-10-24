using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PantryPress.Gateway.Hosting;

/// <summary>
/// The gateway's clock (PP1.12). The rest of PantryPress reads time through the Domain's <c>IClock</c>, but the gateway
/// deliberately holds no reference to the Domain assembly (gateway.md), so this is its one reader of
/// <see cref="TimeProvider"/>, exactly as <c>TimeProviderClock</c> is everyone else's. The cookie handlers read the same
/// provider, so a test that moves it moves the session's sign-in time, its renewals, and its expiry together.
/// </summary>
internal sealed class GatewayClock(TimeProvider time)
{
#pragma warning disable RS0030 // The gateway's one reader of TimeProvider's time (it has no IClock to inject).
    public DateTimeOffset UtcNow => time.GetUtcNow().ToUniversalTime();

    /// <summary>The provider a test registered, or the system's.</summary>
    public static TimeProvider ProviderFrom(IServiceProvider services) =>
        services.GetService<TimeProvider>() ?? TimeProvider.System;
#pragma warning restore RS0030
}

internal static class GatewayClockExtensions
{
    public static IServiceCollection AddGatewayClock(this IServiceCollection services)
    {
        services.TryAddSingleton(sp => new GatewayClock(GatewayClock.ProviderFrom(sp)));
        return services;
    }
}
