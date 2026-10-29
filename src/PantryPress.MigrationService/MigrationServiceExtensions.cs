using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Platform;
using PantryPress.Domain.Modules.Sites;
using PantryPress.ServiceDefaults.Caching;

namespace PantryPress.MigrationService;

internal static class MigrationServiceExtensions
{
    public const string ConnectionName = "pantrypress";

    /// <summary>
    /// Registers <see cref="PantryPressDbContext"/> as every host does, one per scope (PP2.5): connection string from
    /// the <c>pantrypress</c> reference, and the Aspire client's retrying execution strategy and tracing. No health
    /// check, because this one-shot process serves no endpoints. The Identity schema is pinned so the model matches the
    /// migrations. It works on no site, so a site-owned read or write here throws.
    /// </summary>
    public static IHostApplicationBuilder AddMigrationDbContext(this IHostApplicationBuilder builder)
    {
        builder.Services.AddAccountsIdentitySchema();
        builder.Services.AddPantryPressDbContext((services, options) => options.UseSqlServer(
            services.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionName)
                ?? throw new InvalidOperationException($"The migration service has no {ConnectionName} connection string."),
            UseMigrationsAssembly));
        builder.EnrichSqlServerDbContext<PantryPressDbContext>(settings => settings.DisableHealthChecks = true);
        return builder;
    }

    /// <summary>
    /// Registers the local development seed (PP3.9a): two accounts with confirmed addresses and two sites the first owns,
    /// applied after the platform-role list. The options are bound and checked in every environment, so a host outside
    /// Development that finds <c>DevSeed</c> configuration refuses to start; only a Development host registers what the
    /// seed works through - the Accounts module's development-only account operation, the Sites module, and the cache
    /// connection the Sites module refreshes the public tier's host map through (PP2.12). A deployed job therefore
    /// registers exactly what it registered before PP3.9a and runs the seeder that does nothing.
    /// <para>
    /// That rests on the environment: every deployment sets it to <c>Production</c> or <c>Staging</c>
    /// (<c>docs/infrastructure/terraform/pantrypress/locals.tf</c>), never <c>Development</c>. A deployed job told
    /// otherwise would want the local zone and the cache, and refuse to start for want of them - fail closed, with
    /// every dependent unstarted, which is the safe way round.
    /// </para>
    /// </summary>
    public static IHostApplicationBuilder AddDevelopmentSeed(this IHostApplicationBuilder builder)
    {
        var isDevelopmentHost = builder.Environment.IsDevelopment();
        builder.Services.AddDevelopmentSeed(
            builder.Configuration.GetSection(DevelopmentSeedOptions.SectionName),
            isDevelopmentHost);

        if (isDevelopmentHost)
        {
            builder.AddCacheConnection();
            builder.Services.AddAccountsDevelopmentSeed();

            // The same registration the API uses, so the seed's sites are created exactly as PP2.10 creates one. It
            // needs Sites:SubdomainZone, which the AppHost sets for this job in local Development only.
            builder.Services.AddSites();
        }

        return builder;
    }

    /// <summary>Registers the one-shot worker and the exit code it reports.</summary>
    public static IServiceCollection AddMigrationWorker(this IServiceCollection services)
    {
        services.AddSingleton<MigrationOutcome>();
        services.AddHostedService<MigrationWorker>();
        return services;
    }

    // Migrations are compiled into this assembly, so no other host can find them to apply.
    public static void UseMigrationsAssembly(SqlServerDbContextOptionsBuilder sql) =>
        sql.MigrationsAssembly(typeof(MigrationWorker).Assembly.GetName().Name);
}
