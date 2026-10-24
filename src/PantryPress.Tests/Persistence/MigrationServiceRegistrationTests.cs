using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Persistence;

// PP0.6, PP1.1: PantryPressDbContext as the migration service registers it (backend.md). No database is needed.
public sealed class MigrationServiceRegistrationTests
{
    // Never connected to; only parsed.
    private const string UnusedConnectionString = "Server=127.0.0.1,1;Database=pantrypress;Integrated Security=false";

    [Fact]
    public void Design_time_model_matches_the_migrations()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        // PP1.4 adds the second one, which seeds the PlatformAdmin role row (ACCT-005); PP1.9 the idempotency keys; PP1.10
        // the transactional outbox.
        Assert.Collection(
            db.Database.GetMigrations(),
            first => Assert.EndsWith("_InitialIdentity", first, StringComparison.Ordinal),
            second => Assert.EndsWith("_PlatformAdminRole", second, StringComparison.Ordinal),
            third => Assert.EndsWith("_IdempotencyRecords", third, StringComparison.Ordinal),
            fourth => Assert.EndsWith("_Outbox", fourth, StringComparison.Ordinal));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    // PP1.1: Identity builds its tables from the host's IdentityOptions, so the host's model can drift from the
    // design-time one if the schema pin is missing.
    [Fact]
    public async Task Registered_context_model_matches_the_migrations()
    {
        using var host = BuildHost();
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Context_uses_the_migration_service_assembly_and_a_retrying_execution_strategy()
    {
        using var host = BuildHost();
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();

        Assert.Equal(typeof(MigrationWorker).Assembly, db.GetService<IMigrationsAssembly>().Assembly);
        Assert.True(db.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [Fact]
    public void Design_time_factory_uses_the_migration_service_assembly()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        Assert.Equal(typeof(MigrationWorker).Assembly, db.GetService<IMigrationsAssembly>().Assembly);
    }

    [Fact]
    public void Context_registers_no_health_check()
    {
        using var host = BuildHost();

        var registrations = host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        Assert.Empty(registrations);
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration[$"ConnectionStrings:{MigrationServiceExtensions.ConnectionName}"] = UnusedConnectionString;
        builder.AddPantryPressDbContext();
        return builder.Build();
    }
}
