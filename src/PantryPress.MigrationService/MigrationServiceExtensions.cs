using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Accounts;

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
