using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PantryPress.Domain.Managers.Tenancy;

public static class TenancyServiceCollectionExtensions
{
    /// <summary>
    /// The scoped site context (PP2.4): one <see cref="SiteContext"/> per scope, read as <see cref="ISiteContext"/> and
    /// set through <see cref="ISiteContextWriter"/>. Whatever reads or sets it calls this, so no host can forget it.
    /// </summary>
    public static IServiceCollection AddSiteContext(this IServiceCollection services)
    {
        services.TryAddScoped<SiteContext>();
        services.TryAddScoped<ISiteContext>(sp => sp.GetRequiredService<SiteContext>());
        services.TryAddScoped<ISiteContextWriter>(sp => sp.GetRequiredService<SiteContext>());
        return services;
    }
}
