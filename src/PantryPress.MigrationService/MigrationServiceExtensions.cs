using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PantryPress.Domain.Managers.Persistence;

namespace PantryPress.MigrationService;

internal static class MigrationServiceExtensions
{
    public const string ConnectionName = "pantrypress";

    /// <summary>
    /// Registers <see cref="PantryPressDbContext"/> through the Aspire client: connection string from the
    /// <c>pantrypress</c> reference, retrying execution strategy, and tracing. No health check, because this
    /// one-shot process serves no endpoints.
    /// </summary>
    public static IHostApplicationBuilder AddPantryPressDbContext(this IHostApplicationBuilder builder)
    {
        builder.AddSqlServerDbContext<PantryPressDbContext>(
            ConnectionName,
            settings => settings.DisableHealthChecks = true,
            options => options.UseSqlServer(UseMigrationsAssembly));
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
