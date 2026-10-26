using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Accounts;

namespace PantryPress.MigrationService;

/// <summary>
/// Used only by <c>dotnet ef</c> to author migrations. No connection string: adding a migration never touches a
/// database, and applying one happens only through <see cref="MigrationWorker"/> under Aspire. The application
/// services carry only the Identity schema pin, so the model matches the one the hosts build. The context comes from
/// the same registration the hosts use, in a scope that works on no site (PP2.5): authoring a migration reads no rows.
/// </summary>
internal sealed class PantryPressDesignTimeFactory : IDesignTimeDbContextFactory<PantryPressDbContext>
{
    public PantryPressDbContext CreateDbContext(string[] args)
    {
        var services = new ServiceCollection();
        services.AddAccountsIdentitySchema();
        services.AddPantryPressDbContext((_, options) => options.UseSqlServer(MigrationServiceExtensions.UseMigrationsAssembly));

        // The tool disposes the context and then exits; the provider and scope live as long as the process.
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<PantryPressDbContext>();
    }
}
