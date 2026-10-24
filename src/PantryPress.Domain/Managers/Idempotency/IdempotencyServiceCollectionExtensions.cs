using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PantryPress.Domain.Managers.Idempotency;

public static class IdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// The idempotency-key store (PP1.9), with its retention and lease from <paramref name="configuration"/>. Needs
    /// <c>PantryPressDbContext</c> and <c>IClock</c> registered by the host.
    /// </summary>
    public static IServiceCollection AddIdempotency(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdempotencyOptions>()
            .Bind(configuration)
            .Validate(
                o => o.IsValid(),
                "Idempotency:Lease must be positive and shorter than Idempotency:Retention.")
            .ValidateOnStart();

        services.TryAddSingleton<IIdempotencyStore, IdempotencyStore>();
        return services;
    }
}
