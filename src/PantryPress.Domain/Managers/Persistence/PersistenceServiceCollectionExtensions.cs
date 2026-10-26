using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Tenancy;

namespace PantryPress.Domain.Managers.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PantryPressDbContext"/> one per scope with the site context it reads (PP2.5). Never pooled,
    /// nor made by a singleton factory: the global site filter and the save's stamp read the scope's site, which a pooled
    /// or factory-made context would not have (the context refuses pooled options, and a test refuses the rest). A host
    /// adds the Aspire client's retries, tracing, and health check with <c>EnrichSqlServerDbContext</c>, which keeps this
    /// registration as it is.
    /// </summary>
    public static IServiceCollection AddPantryPressDbContext(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> configure)
    {
        services.AddSiteContext();
        services.AddDbContext<PantryPressDbContext>(configure);
        return services;
    }
}
