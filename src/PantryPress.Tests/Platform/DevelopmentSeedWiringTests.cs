using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using PantryPress.Tests.Hosting;

namespace PantryPress.Tests.Platform;

// PP3.9a, the first of the three guards: the development seed's password is declared in local Development only, so it
// is in no published manifest and no deployment, and it reaches the migration job and nothing else (aspire.md: no
// hard-coded secrets, no secret in a manifest).
public sealed class DevelopmentSeedWiringTests
{
    private const string Variable = EphemeralAppHost.DevelopmentSeedVariable;

    private const string ZoneVariable = "Sites__SubdomainZone";

    [Fact]
    public async Task The_password_goes_only_to_the_migration_job_as_a_secret()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>(
            ["--DevSeed:Password=not-a-real-password"]);

        var migrations = await EnvironmentAsync(appHost, "migrationservice");

        var parameter = Assert.IsType<ParameterResource>(migrations[Variable]);
        Assert.Equal("dev-seed-password", parameter.Name);
        Assert.True(parameter.Secret);
        Assert.Equal("not-a-real-password", await ValueAsync(parameter));
        foreach (var name in new[] { "apiservice", "site", "worker", "gateway" })
        {
            Assert.DoesNotContain(Variable, (await EnvironmentAsync(appHost, name)).Keys);
        }
    }

    // The seed creates its sites through the Sites module (PP2.10), so the job gets the zone they answer under and the
    // cache their host map lives in (PP2.12) - and never waits for the cache (aspire.md).
    [Fact]
    public async Task The_migration_job_gets_the_local_zone_and_the_cache_but_waits_for_neither()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();

        var migrations = appHost.Resources.Single(r => r.Name == "migrationservice");

        Assert.Equal("localhost", Assert.IsType<string>((await EnvironmentAsync(appHost, "migrationservice"))[ZoneVariable]));
        Assert.Contains(
            migrations.Annotations.OfType<ResourceRelationshipAnnotation>(),
            r => r.Type == "Reference" && r.Resource.Name == "redis");
        Assert.DoesNotContain(migrations.Annotations.OfType<WaitAnnotation>(), w => w.Resource.Name == "redis");
    }

    [Fact]
    public async Task Publishing_has_no_seed_password_and_no_local_zone()
    {
        var outputPath = Directory.CreateTempSubdirectory("pantrypress-publish-").FullName;
        try
        {
            await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>(
                ["--operation", "publish", "--output-path", outputPath, "--DevSeed:Password=not-a-real-password"]);

            var migrations = await EnvironmentAsync(appHost, "migrationservice");

            Assert.DoesNotContain(Variable, migrations.Keys);
            Assert.DoesNotContain(ZoneVariable, migrations.Keys);
            Assert.DoesNotContain(appHost.Resources.OfType<ParameterResource>(), p => p.Name == "dev-seed-password");
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }

    private static async Task<string?> ValueAsync(ParameterResource parameter) =>
        await ((IValueProvider)parameter).GetValueAsync(CancellationToken.None);

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
