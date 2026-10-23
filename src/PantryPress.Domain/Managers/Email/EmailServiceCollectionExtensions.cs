using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PantryPress.Domain.Managers.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IEmailQueue"/> and the background worker that drains it. The host registers the
    /// <see cref="IEmailGateway"/> itself, and must also call <c>AddApplicationClock</c> for retry delays.
    /// </summary>
    public static IServiceCollection AddEmailQueue(this IServiceCollection services)
    {
        services.TryAddSingleton<InProcessEmailQueue>();
        services.TryAddSingleton<IEmailQueue>(sp => sp.GetRequiredService<InProcessEmailQueue>());
        services.AddHostedService<EmailDispatchWorker>();
        return services;
    }
}
