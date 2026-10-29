using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Platform;
using PantryPress.Domain.Modules.Sites;
using PantryPress.MigrationService;
using PantryPress.ServiceDefaults.Caching;
using StackExchange.Redis;

namespace PantryPress.Tests.Persistence;

// Services for reading the database of a running test AppHost after the migration service has finished: the context
// and the Identity stores registered as a host will register them. The caller disposes the provider.
internal static class TestDatabase
{
    public static async Task<ServiceProvider> CreateServicesAsync(
        DistributedApplication app,
        CancellationToken cancellationToken)
    {
        var connectionString = await app.GetConnectionStringAsync(MigrationServiceExtensions.ConnectionName, cancellationToken)
            ?? throw new InvalidOperationException("The test AppHost has no pantrypress connection string.");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountsIdentitySchema();
        services.AddPantryPressDbContext((_, options) => options.UseSqlServer(
            connectionString,
            MigrationServiceExtensions.UseMigrationsAssembly));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<PantryPressDbContext>();
        return services.BuildServiceProvider();
    }

    // Services for the operator's platform-role path (PP1.4) against the same migrated database: the audited facade,
    // the list reconciliation, and fake logging to read the audit lines. Composed exactly as the migration job does,
    // with no key ring of its own, so these tests exercise UnusedDataProtection and fail if a role change ever reaches
    // for a protector.
    public static async Task<ServiceProvider> CreateOperatorServicesAsync(
        DistributedApplication app,
        string admins,
        CancellationToken cancellationToken)
    {
        var connectionString = await app.GetConnectionStringAsync(MigrationServiceExtensions.ConnectionName, cancellationToken)
            ?? throw new InvalidOperationException("The test AppHost has no pantrypress connection string.");

        var services = new ServiceCollection();
        services.AddFakeLogging();
        services.AddApplicationClock();
        services.AddPantryPressDbContext((_, options) => options.UseSqlServer(
            connectionString,
            MigrationServiceExtensions.UseMigrationsAssembly));
        services.AddPlatformAdministration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Admins"] = admins })
            .Build());
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The local development seed over the same migrated database (PP3.9a), composed exactly as the migration job's
    /// Development run composes it: the operator's platform-role path with no list, the seed itself, the Accounts
    /// module's development-only account operation, and the Sites module as the API registers it - so the sites it
    /// makes are made the way PP2.10 makes one, host-map refresh included.
    /// </summary>
    /// <param name="password">The seed's password, or empty for a run with none configured.</param>
    public static async Task<ServiceProvider> CreateDevelopmentSeedServicesAsync(
        DistributedApplication app,
        string password,
        CancellationToken cancellationToken)
    {
        var connectionString = await app.GetConnectionStringAsync(MigrationServiceExtensions.ConnectionName, cancellationToken)
            ?? throw new InvalidOperationException("The test AppHost has no pantrypress connection string.");
        var cacheConnection = await app.GetConnectionStringAsync(CacheConnection.ConnectionName, cancellationToken)
            ?? throw new InvalidOperationException("The test AppHost has no redis connection string.");

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sites:SubdomainZone"] = SubdomainZone })
            .Build());
        services.AddFakeLogging();
        services.AddApplicationClock();
        services.AddPantryPressDbContext((_, options) => options.UseSqlServer(
            connectionString,
            MigrationServiceExtensions.UseMigrationsAssembly));
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(cacheConnection));
        services.AddPlatformAdministration(new ConfigurationBuilder().Build());
        services.AddDevelopmentSeed(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Password"] = password })
                .Build(),
            isDevelopmentHost: true);
        services.AddAccountsDevelopmentSeed();
        services.AddSites();
        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>The zone the seed's sites answer under locally (SITE-009), as the AppHost sets it for the job.</summary>
    public const string SubdomainZone = "localhost";
}
