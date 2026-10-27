using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.DataLayer;
using PantryPress.Domain.Modules.Sites.Facades;
using PantryPress.Domain.Modules.Sites.Repositories;
using PantryPress.Domain.Modules.Sites.Validation;

namespace PantryPress.Domain.Modules.Sites;

public static class SitesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Sites module (PP2.1): the site is the tenant (PP-D03). It answers the caller's membership of the site a
    /// route names (PP2.3), which the admin API's site policies ask, and which sets the request's site context (PP2.4);
    /// and it creates sites and reads them for their members (PP2.10). ApiService calls this because admin site routes
    /// live there; other hosts call it when they need something from it. <see cref="SitesOptions"/> is bound from the
    /// <c>Sites</c> section of the host's configuration and checked when a host starts, so every host that calls this
    /// sets <c>Sites:SubdomainZone</c>. The host also registers <c>PantryPressDbContext</c> and the clock.
    /// </summary>
    public static IServiceCollection AddSites(this IServiceCollection services)
    {
        services.AddSiteContext();
        services.AddAuditLog();
        services.AddOptions<SitesOptions>()
            .BindConfiguration(SitesOptions.SectionName)
            .Validate(o => o.HasValidZone(), "Sites:SubdomainZone must be a host's zone in stored form, such as pantrypress.app.")
            .Validate(o => o.UnconfirmedAccountSiteLimit >= 0, "Sites:UnconfirmedAccountSiteLimit must not be negative.")
            .ValidateOnStart();

        services.AddScoped<SiteRepository>();
        services.AddScoped<SiteMemberRepository>();
        services.AddScoped<SiteHostRepository>();
        services.AddScoped<SitesUnitOfWork>();
        services.AddScoped<SiteMembershipDataLayer>();
        services.AddScoped<SitesDataLayer>();
        services.AddScoped<IValidator<CreateSiteRequest>, CreateSiteRequestValidator>();
        services.AddScoped<IValidator<ListSitesQuery>, ListSitesQueryValidator>();
        services.AddScoped<IValidator<SlugAvailabilityQuery>, SlugAvailabilityQueryValidator>();
        services.AddScoped<ISiteMembershipFacade, SiteMembershipFacade>();
        services.AddScoped<ISitesFacade, SitesFacade>();
        return services;
    }
}
