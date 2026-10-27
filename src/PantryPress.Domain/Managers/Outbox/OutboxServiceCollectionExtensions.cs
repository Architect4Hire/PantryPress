using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PantryPress.Domain.Managers.Tenancy;

namespace PantryPress.Domain.Managers.Outbox;

public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// <see cref="IOutbox"/> for writing messages (PP1.10), scoped to share the request's context and its site
    /// context (PP2.4). Needs <c>PantryPressDbContext</c> and <c>IClock</c> registered by the host.
    /// </summary>
    public static IServiceCollection AddOutbox(this IServiceCollection services)
    {
        services.AddSiteContext();
        services.TryAddScoped<IOutbox, Outbox>();
        return services;
    }

    /// <summary>
    /// The dispatcher's <see cref="IOutboxStore"/>, with its claim, retry, and retention settings from
    /// <paramref name="configuration"/>. Only the Worker calls this.
    /// </summary>
    public static IServiceCollection AddOutboxStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OutboxOptions>()
            .Bind(configuration)
            .Validate(
                o => o.IsValid(),
                "Outbox settings must be positive, BatchSize at most 100, PurgeBatchSize at most 5000, and RetryCap at " +
                "least RetryBase.")
            .ValidateOnStart();

        services.TryAddSingleton<IOutboxStore, OutboxStore>();
        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> to deliver <typeparamref name="TMessage"/> in the Worker. A message type
    /// has exactly one handler: registering a second for the same <see cref="IOutboxMessage.OutboxType"/> fails the
    /// Worker's start. Each delivery's scope works on the site its message was staged under, if any.
    /// </summary>
    public static IServiceCollection AddOutboxHandler<TMessage, THandler>(this IServiceCollection services)
        where TMessage : IOutboxMessage
        where THandler : class, IOutboxHandler<TMessage>
    {
        services.AddSiteContext();
        services.AddScoped<IOutboxHandler<TMessage>, THandler>();
        services.AddSingleton(OutboxHandlerRegistration.For<TMessage>());
        return services;
    }
}
