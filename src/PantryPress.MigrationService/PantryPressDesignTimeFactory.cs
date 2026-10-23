using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Accounts;

namespace PantryPress.MigrationService;

/// <summary>
/// Used only by <c>dotnet ef</c> to author migrations. No connection string: adding a migration never touches a
/// database, and applying one happens only through <see cref="MigrationWorker"/> under Aspire. The application
/// services carry only the Identity schema pin, so the model matches the one the hosts build.
/// </summary>
internal sealed class PantryPressDesignTimeFactory : IDesignTimeDbContextFactory<PantryPressDbContext>
{
    public PantryPressDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PantryPressDbContext>()
            .UseSqlServer(MigrationServiceExtensions.UseMigrationsAssembly)
            .UseApplicationServiceProvider(new ServiceCollection().AddAccountsIdentitySchema().BuildServiceProvider())
            .Options);
}
