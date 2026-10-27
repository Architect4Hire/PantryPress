using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
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
        // the transactional outbox; PP1.11 the append-only audit log; PP1.12 the signed-out sessions; PP2.2 the sites;
        // PP2.3 their members; PP2.8 the reserved slugs.
        Assert.Collection(
            db.Database.GetMigrations(),
            first => Assert.EndsWith("_InitialIdentity", first, StringComparison.Ordinal),
            second => Assert.EndsWith("_PlatformAdminRole", second, StringComparison.Ordinal),
            third => Assert.EndsWith("_IdempotencyRecords", third, StringComparison.Ordinal),
            fourth => Assert.EndsWith("_Outbox", fourth, StringComparison.Ordinal),
            fifth => Assert.EndsWith("_AuditLog", fifth, StringComparison.Ordinal),
            sixth => Assert.EndsWith("_SessionRevocations", sixth, StringComparison.Ordinal),
            seventh => Assert.EndsWith("_Sites", seventh, StringComparison.Ordinal),
            eighth => Assert.EndsWith("_SiteMembers", eighth, StringComparison.Ordinal),
            ninth => Assert.EndsWith("_ReservedSlugs", ninth, StringComparison.Ordinal));
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

    // PP2.5: one context per scope, bound to that scope's site context - here always none.
    [Fact]
    public async Task Context_is_made_per_scope_with_that_scopes_site_context()
    {
        using var host = BuildHost();
        await using var first = host.Services.CreateAsyncScope();
        await using var second = host.Services.CreateAsyncScope();
        var db = first.ServiceProvider.GetRequiredService<PantryPressDbContext>();

        Assert.Same(first.ServiceProvider.GetRequiredService<ISiteContext>(), ((ISiteScopedDbContext)db).Site);
        Assert.NotSame(db, second.ServiceProvider.GetRequiredService<PantryPressDbContext>());
        Assert.Null(((ISiteScopedDbContext)db).Site.Current);
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
        builder.AddMigrationDbContext();
        return builder.Build();
    }
}
