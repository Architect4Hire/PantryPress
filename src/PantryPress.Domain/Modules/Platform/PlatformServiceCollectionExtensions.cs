using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Platform.Contracts;
using PantryPress.Domain.Modules.Platform.Facades;

namespace PantryPress.Domain.Modules.Platform;

public static class PlatformServiceCollectionExtensions
{
    /// <summary>
    /// Registers the operator's platform-role administration (ACCT-005; PP1.4): the audited facade, the list
    /// reconciliation, and the narrow Accounts role contract it needs. Only the migration job calls this. No
    /// request-serving host does, which is what makes the assignment path operator-only rather than merely
    /// authorized. The <paramref name="configuration"/> section binds <see cref="PlatformOptions"/>.
    /// </summary>
    public static IServiceCollection AddPlatformAdministration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<PlatformOptions>()
            .Bind(configuration)
            .Validate(o => o.IsValid(), "Platform:Admins must be email addresses separated by commas, semicolons, or new lines.")
            .ValidateOnStart();

        services.AddAccountRoles();
        services.AddScoped<IPlatformAdminFacade, PlatformAdminFacade>();
        services.AddScoped<IPlatformAdminReconciler, PlatformAdminReconciler>();
        return services;
    }

    /// <summary>
    /// Registers the local development seed (PP3.9a): two accounts with confirmed addresses and two sites the first owns,
    /// applied by the migration job after the platform-role list. Only the migration job calls this, and only its
    /// Development run gets the real seeder.
    /// <para>
    /// Three guards keep it out of every other environment. The AppHost declares the password parameter in local
    /// Development alone, so it is in no manifest and no deployment. <paramref name="isDevelopmentHost"/> comes from the
    /// host's own <c>IHostEnvironment</c> - never from configuration, which is why
    /// <see cref="DevelopmentSeedOptions.Enabled"/> has no setter - and anywhere else this binds
    /// <see cref="DisabledDevelopmentSeeder"/>, which does nothing, and registers nothing the seed would need. And the
    /// options are checked when the calling host starts, whatever its environment, so it refuses to start rather than
    /// ignore seed configuration it must not act on. Only the migration job calls this, and the AppHost gives the
    /// setting to that job alone, so that is the host that refuses.
    /// </para>
    /// <para>
    /// The caller registers what the seed works through: the Accounts module's development-only account operation
    /// (<c>AddAccountsDevelopmentSeed</c>) and the Sites module (<c>AddSites</c>), both on a Development host only.
    /// </para>
    /// </summary>
    /// <param name="configuration">The <c>DevSeed</c> section, which binds <see cref="DevelopmentSeedOptions"/>.</param>
    /// <param name="isDevelopmentHost">Whether this host's environment is Development.</param>
    public static IServiceCollection AddDevelopmentSeed(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopmentHost)
    {
        var options = services.AddOptions<DevelopmentSeedOptions>().Bind(configuration);
        if (isDevelopmentHost)
        {
            options.PostConfigure(o => o.MarkDevelopmentHost());
        }

        options
            .Validate(
                o => o.IsAllowedHere(),
                $"{DevelopmentSeedOptions.SectionName} configuration is local Development only (PP3.9a). Remove it, or "
                + "run this host in Development.")
            .ValidateOnStart();

        if (isDevelopmentHost)
        {
            services.AddScoped<IDevelopmentSeeder, DevelopmentSeeder>();
        }
        else
        {
            services.AddScoped<IDevelopmentSeeder, DisabledDevelopmentSeeder>();
        }

        return services;
    }
}
