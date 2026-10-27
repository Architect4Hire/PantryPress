using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Email;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Business;
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
    /// live there; other hosts call it when they need something from it. It invites people to a site and lets them join
    /// (PP2.11), staging each invitation's email in the outbox for the Worker, and lets an Owner list, re-role, and remove
    /// members (PP2.11b), with their addresses from Accounts' directory of addresses, which the host registers with Accounts. <see cref="SitesOptions"/> is bound from the
    /// <c>Sites</c> section of the host's configuration and checked when a host starts, so every host that calls this
    /// sets <c>Sites:SubdomainZone</c>. The host also registers <c>PantryPressDbContext</c>, the clock, and data protection
    /// with the application name the Worker shares, since the API checks the invitation links the Worker makes.
    /// </summary>
    public static IServiceCollection AddSites(this IServiceCollection services)
    {
        services.AddSiteContext();
        services.AddAuditLog();
        services.AddOutbox();
        services.AddOptions<SitesOptions>()
            .BindConfiguration(SitesOptions.SectionName)
            .Validate(o => o.HasValidZone(), "Sites:SubdomainZone must be a host's zone in stored form, such as pantrypress.app.")
            .Validate(o => o.UnconfirmedAccountSiteLimit >= 0, "Sites:UnconfirmedAccountSiteLimit must not be negative.")
            .Validate(o => o.HasValidInvitationLifetime(), "Sites:InvitationLifetime must be between one hour and 30 days.")
            .Validate(o => o.MaxOpenInvitations > 0, "Sites:MaxOpenInvitations must be positive.")
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

        services.AddScoped<SiteInvitationRepository>();
        services.AddScoped<SiteInvitationTokens>();
        services.AddScoped<SiteInvitationsDataLayer>();
        services.AddScoped<IValidator<InviteMemberRequest>, InviteMemberRequestValidator>();
        services.AddScoped<IValidator<AcceptInvitationRequest>, AcceptInvitationRequestValidator>();
        services.AddScoped<SiteInvitationAcceptance>();
        services.AddScoped<ISiteInvitationsFacade, SiteInvitationsFacade>();

        services.AddScoped<SiteMembersDataLayer>();
        services.AddScoped<IValidator<ListSiteMembersQuery>, ListSiteMembersQueryValidator>();
        services.AddScoped<IValidator<ChangeMemberRoleRequest>, ChangeMemberRoleRequestValidator>();
        services.AddScoped<ISiteMembersFacade, SiteMembersFacade>();
        return services;
    }

    /// <summary>
    /// What the Worker does for the Sites module (PP2.11): send invitation email from the outbox, with the link in it. The
    /// <paramref name="configuration"/> section binds <see cref="SiteInvitationEmailOptions"/> - the admin app's origin for
    /// the link - checked at startup. The host also registers <c>PantryPressDbContext</c>, the clock, the outbox dispatcher,
    /// an <see cref="IEmailGateway"/>, and data protection with the API's application name, since the API checks the links
    /// made here.
    /// </summary>
    /// <remarks>
    /// <paramref name="sendsEmail"/> is false where there is no email provider yet, as for account email (PP1.12): the
    /// Worker then claims no invitation email, which waits in the outbox until a provider is configured.
    /// </remarks>
    public static IServiceCollection AddSitesWorker(this IServiceCollection services, IConfiguration configuration, bool sendsEmail)
    {
        if (sendsEmail)
        {
            services.AddSiteInvitationEmailDelivery(configuration);
        }

        return services;
    }

    /// <summary>
    /// The outbox handler that sends invitation email, and what it reads to build it (PP2.11). Part of
    /// <see cref="AddSitesWorker"/>; on its own only for a test host that already registered <see cref="AddSites"/> and
    /// delivers its own outbox.
    /// </summary>
    internal static IServiceCollection AddSiteInvitationEmailDelivery(this IServiceCollection services, IConfiguration configuration)
    {
        // Both checked at startup, as for account email: a host that sends invitation email has the link origin and a
        // provider, or it refuses to start rather than claim the messages and poison every one for want of either.
        services.AddOptions<SiteInvitationEmailOptions>()
            .Bind(configuration)
            .Validate(o => o.IsValid(), "Sites:AppBaseUrl must be the admin app's absolute HTTPS origin.")
            .Validate<IServiceProvider>(
                (_, provider) => provider.GetService<IEmailGateway>() is not null,
                "No email gateway is registered. Invitation email is sent from the outbox here (PP2.11); outside "
                + "Development a provider arrives with PP13.1.")
            .ValidateOnStart();
        services.AddSiteContext();
        services.AddSingleton(sp => new SiteInvitationEmails(sp.GetRequiredService<IOptions<SiteInvitationEmailOptions>>().Value.AppBaseUrl));
        services.TryAddScoped<SiteRepository>();
        services.TryAddScoped<SiteInvitationRepository>();
        services.TryAddScoped<SiteInvitationTokens>();
        services.AddScoped<SiteInvitationMailDataLayer>();
        services.AddOutboxHandler<SiteInvitationEmailMessage, SiteInvitationEmailHandler>();
        return services;
    }
}
