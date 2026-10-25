using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.DataLayer;
using PantryPress.Domain.Modules.Sites.Facades;
using PantryPress.Domain.Modules.Sites.Repositories;

namespace PantryPress.Domain.Modules.Sites;

public static class SitesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Sites module (PP2.1): the site is the tenant (PP-D03). So far it answers one question, the caller's
    /// membership of the site a route names (PP2.3), which the admin API's site policies ask, and which sets the
    /// request's site context (PP2.4). ApiService calls this because admin site routes live there; other hosts call it
    /// when they need something from it.
    /// </summary>
    public static IServiceCollection AddSites(this IServiceCollection services)
    {
        services.AddSiteContext();
        services.AddScoped<SiteRepository>();
        services.AddScoped<SiteMemberRepository>();
        services.AddScoped<SiteMembershipDataLayer>();
        services.AddScoped<ISiteMembershipFacade, SiteMembershipFacade>();
        return services;
    }
}
