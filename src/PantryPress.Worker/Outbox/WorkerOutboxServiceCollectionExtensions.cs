using Microsoft.Extensions.DependencyInjection.Extensions;
using PantryPress.Domain.Managers.Outbox;

namespace PantryPress.Worker.Outbox;

internal static class WorkerOutboxServiceCollectionExtensions
{
    /// <summary>
    /// The outbox dispatcher and the periodic sweep (PP1.10), over the store configured from
    /// <paramref name="configuration"/>. Handlers are registered with <c>AddOutboxHandler</c>. Needs the database
    /// context, <c>IClock</c>, and the idempotency store registered by the host.
    /// </summary>
    public static IServiceCollection AddOutboxDispatcher(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOutbox();
        services.AddOutboxStore(configuration);
        services.TryAddSingleton<OutboxMetrics>();
        services.TryAddSingleton<OutboxProcessor>();
        services.AddHostedService<OutboxDispatcher>();
        services.AddHostedService<OutboxMaintenance>();
        return services;
    }
}
