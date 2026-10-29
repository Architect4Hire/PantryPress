using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Platform.Business;
using PantryPress.Tests.Persistence;

namespace PantryPress.Tests.Hosting;

// Inside the namespace, so the Sites module's types win over the PantryPress.Tests.Site namespace.
using PantryPress.Domain.Modules.Sites;

// PP0.6: the migration service runs once against the pantrypress database and everything else waits for it to
// finish cleanly (aspire.md, OPS-003).
public sealed class MigrationServiceTests
{
    private static readonly string[] Dependents = ["apiservice", "site", "worker", "gateway", "admin"];

    private const string DevelopmentSeedPassword = "Not-a-real-password-PP39a-job";

    [Fact]
    public async Task Migration_service_references_and_waits_for_the_database()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();

        var resource = appHost.Resources.Single(r => r.Name == "migrationservice");

        Assert.Contains(
            resource.Annotations.OfType<ResourceRelationshipAnnotation>(),
            r => r.Type == "Reference" && r.Resource.Name == "pantrypress");
        Assert.Contains(
            resource.Annotations.OfType<WaitAnnotation>(),
            w => w.WaitType == WaitType.WaitUntilHealthy && w.Resource.Name == "pantrypress");
    }

    [Fact]
    public async Task Every_project_waits_for_migrations_to_complete_successfully()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();

        foreach (var name in Dependents)
        {
            var resource = appHost.Resources.Single(r => r.Name == name);
            Assert.Contains(
                resource.Annotations.OfType<WaitAnnotation>(),
                w => w.WaitType == WaitType.WaitForCompletion && w.Resource.Name == "migrationservice" && w.ExitCode == 0);
        }
    }

    // The development seed runs here too (PP3.9a), rather than in a class of its own: it needs the job to have run in a
    // real AppHost, and starting a second one beside this and the shared fixture's outruns a developer machine. What the
    // seed writes is DevelopmentSeedTests'; this proves the job runs it and still exits cleanly.
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Run_applies_every_migration_seeds_development_exits_cleanly_and_dependents_start_after_it()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var appHost = await EphemeralAppHost.CreateAsync(
            timeout.Token,
            developmentSeedPassword: DevelopmentSeedPassword);
        await using var app = await appHost.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);

        var finished = await app.ResourceNotifications.WaitForResourceAsync(
            "migrationservice",
            e => KnownResourceStates.TerminalStates.Contains(e.Snapshot.State?.Text),
            timeout.Token);

        Assert.Equal(KnownResourceStates.Finished, finished.Snapshot.State?.Text);
        Assert.Equal(0, finished.Snapshot.ExitCode);

        await using (var services = await TestDatabase.CreateServicesAsync(app, timeout.Token))
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
            Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync(timeout.Token));

            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            foreach (var email in DevelopmentSeedData.Emails)
            {
                var account = await users.FindByEmailAsync(email);
                Assert.NotNull(account);
                Assert.True(account.EmailConfirmed);
                Assert.True(await users.CheckPasswordAsync(account, DevelopmentSeedPassword));
            }

            var hosts = await db.Set<SiteHost>().AsNoTracking().Select(h => h.Host).ToListAsync(timeout.Token);
            foreach (var request in DevelopmentSeedData.SiteRequests)
            {
                Assert.Contains($"{request.Slug}.{TestDatabase.SubdomainZone}", hosts);
            }
        }

        await app.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", timeout.Token);
    }
}
