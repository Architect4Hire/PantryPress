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

    public static async Task<IDistributedApplicationTestingBuilder> CreateAsync(CancellationToken cancellationToken)
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>(
            cancellationToken);

        foreach (var explorer in appHost.Resources.Where(r => DataExplorers.Any(e => e.Name == r.Name)).ToList())
        {
            appHost.Resources.Remove(explorer);
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
