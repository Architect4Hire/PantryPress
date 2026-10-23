using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.MigrationService;

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
        services.AddDbContext<PantryPressDbContext>(options => options.UseSqlServer(
            connectionString,
            MigrationServiceExtensions.UseMigrationsAssembly));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<PantryPressDbContext>();
        return services.BuildServiceProvider();
    }
}
