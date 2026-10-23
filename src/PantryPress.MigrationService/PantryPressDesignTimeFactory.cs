using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PantryPress.Domain.Managers.Persistence;

namespace PantryPress.MigrationService;

/// <summary>
/// Used only by <c>dotnet ef</c> to author migrations. No connection string: adding a migration never touches a
/// database, and applying one happens only through <see cref="MigrationWorker"/> under Aspire.
/// </summary>
internal sealed class PantryPressDesignTimeFactory : IDesignTimeDbContextFactory<PantryPressDbContext>
{
    public PantryPressDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PantryPressDbContext>()
            .UseSqlServer(MigrationServiceExtensions.UseMigrationsAssembly)
            .Options);
}
