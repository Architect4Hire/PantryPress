using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace PantryPress.Tests.Hosting;

// The AppHost for integration tests with the data containers made ephemeral: never attach to the developer's
// persistent containers or volumes, and leave nothing behind. The caller owns the builder, which must outlive the app.
internal static class EphemeralAppHost
{
    public static readonly string[] DataContainers = ["sql", "redis", "storage"];

    // Each developer explorer and the data resource it browses. No test reads through them, so the ephemeral AppHost
    // drops them rather than pull and start two more containers for every integration test.
    public static readonly (string Name, string Parent)[] DataExplorers = [("dbgate", "sql"), ("redisinsight", "redis")];

    /// <summary>The development seed's password as the migration job reads it (PP3.9a).</summary>
    public const string DevelopmentSeedVariable = "DevSeed__Password";

    /// <param name="runWorker">
    /// False for a fixture whose tests move a fake clock over the shared database. The Worker sweeps expired rows on the
    /// real clock (PP1.10), so it would purge idempotency keys a test is still using, its fake hours already behind.
    /// </param>
    /// <param name="developmentSeedPassword">
    /// What the migration job seeds its development accounts with (PP3.9a), or null - the default - for no seed at all.
    /// The password is always decided here, never left to the developer's own user-secrets, which the AppHost's
    /// configuration would otherwise supply: an integration test's database must be the same on every machine.
    /// </param>
    public static async Task<IDistributedApplicationTestingBuilder> CreateAsync(
        CancellationToken cancellationToken,
        bool runWorker = true,
        string? developmentSeedPassword = null)
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>(
            cancellationToken);

        // Last callback wins, so this decides the value whatever the AppHost's parameter resolved to.
        appHost.Resources.Single(r => r.Name == "migrationservice").Annotations.Add(
            new EnvironmentCallbackAnnotation(context =>
                context.EnvironmentVariables[DevelopmentSeedVariable] = developmentSeedPassword ?? string.Empty));

        foreach (var explorer in appHost.Resources.Where(r => DataExplorers.Any(e => e.Name == r.Name)).ToList())
        {
            appHost.Resources.Remove(explorer);
        }

        if (!runWorker)
        {
            appHost.Resources.Remove(appHost.Resources.Single(r => r.Name == "worker"));
        }

        foreach (var resource in appHost.Resources.Where(r => DataContainers.Contains(r.Name)))
        {
            // WithLifetime(Persistent) also adds PersistenceAnnotation, which DCP reads; removing only the
            // lifetime annotation still leaks a persistent container.
#pragma warning disable ASPIREPERSISTENCE001 // Evaluation API; a rename should break this build, not leak containers.
            RemoveAll(resource.Annotations, a => a is ContainerLifetimeAnnotation or PersistenceAnnotation);
#pragma warning restore ASPIREPERSISTENCE001
            RemoveAll(resource.Annotations, a => a is ContainerMountAnnotation { Type: ContainerMountType.Volume });
        }

        return appHost;
    }

    private static void RemoveAll(ResourceAnnotationCollection annotations, Func<IResourceAnnotation, bool> match)
    {
        foreach (var annotation in annotations.Where(match).ToList())
        {
            annotations.Remove(annotation);
        }
    }
}
