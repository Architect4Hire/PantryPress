using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace PantryPress.Tests.Platform;

// PP1.4: the operator's PlatformAdmin list reaches the migration job and nothing else (ACCT-005, aspire.md: no
// hard-coded endpoints or secrets in code).
public sealed class PlatformAdminWiringTests
{
    private const string Variable = "Platform__Admins";

    [Fact]
    public async Task The_configured_list_goes_only_to_the_migration_job()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>(
            ["--Platform:Admins=ops@example.test;second@example.test"]);

        var migrations = await EnvironmentAsync(appHost, "migrationservice");

        Assert.Equal("ops@example.test;second@example.test", Assert.IsType<string>(migrations[Variable]));
        foreach (var name in new[] { "apiservice", "site", "worker", "gateway" })
        {
            Assert.DoesNotContain(Variable, (await EnvironmentAsync(appHost, name)).Keys);
        }
    }

    [Fact]
    public async Task With_nothing_configured_the_job_gets_no_list_and_changes_no_roles()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();

        var migrations = await EnvironmentAsync(appHost, "migrationservice");

        Assert.DoesNotContain(Variable, migrations.Keys);
    }

    private static async Task<Dictionary<string, object>> EnvironmentAsync(
        IDistributedApplicationTestingBuilder appHost,
        string resourceName)
    {
        var resource = (IResourceWithEnvironment)appHost.Resources.Single(r => r.Name == resourceName);
        var context = new EnvironmentCallbackContext(appHost.ExecutionContext, resource);
        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        return context.EnvironmentVariables;
    }
}
