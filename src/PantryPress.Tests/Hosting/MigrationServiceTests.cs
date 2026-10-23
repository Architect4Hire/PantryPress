using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace PantryPress.Tests.Hosting;

// PP0.6: the migration service runs once against the pantrypress database and everything else waits for it to
// finish cleanly (aspire.md, OPS-003).
public sealed class MigrationServiceTests
{
    private static readonly string[] Dependents = ["apiservice", "site", "worker", "gateway", "admin"];

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

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Empty_run_exits_cleanly_and_dependents_start_after_it()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var appHost = await EphemeralAppHost.CreateAsync(timeout.Token);
        await using var app = await appHost.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);

        var finished = await app.ResourceNotifications.WaitForResourceAsync(
            "migrationservice",
            e => KnownResourceStates.TerminalStates.Contains(e.Snapshot.State?.Text),
            timeout.Token);

        Assert.Equal(KnownResourceStates.Finished, finished.Snapshot.State?.Text);
        Assert.Equal(0, finished.Snapshot.ExitCode);

        await app.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", timeout.Token);
    }
}
