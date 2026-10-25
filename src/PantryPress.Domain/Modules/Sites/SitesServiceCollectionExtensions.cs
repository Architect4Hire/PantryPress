using Microsoft.Extensions.DependencyInjection;

namespace PantryPress.Domain.Modules.Sites;

public static class SitesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Sites module (PP2.1): the site is the tenant (PP-D03). Nothing is registered yet; the site itself
    /// (PP2.2), membership and its policies (PP2.3), and the site request context (PP2.4) land here. ApiService calls
    /// this because admin site routes live there; other hosts call it when they need something from it.
    /// </summary>
    public static IServiceCollection AddSites(this IServiceCollection services) => services;
}
