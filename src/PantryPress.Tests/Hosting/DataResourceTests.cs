using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace PantryPress.Tests.Hosting;

// PP0.4: PantryPress's own SQL, Redis, and blob storage (PP-D16, aspire.md).
public sealed class DataResourceTests
{
    private static readonly string[] DataContainers = ["sql", "redis", "storage"];

    private static readonly string[] HealthyResources = ["sql", "pantrypress", "redis", "storage", "blobs"];

    [Fact]
    public async Task Data_containers_are_persistent_with_pantrypress_named_volumes()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();

        foreach (var name in DataContainers)
        {
            var resource = appHost.Resources.Single(r => r.Name == name);

            var lifetime = Assert.Single(resource.Annotations.OfType<ContainerLifetimeAnnotation>());
            Assert.Equal(ContainerLifetime.Persistent, lifetime.Lifetime);

            var volume = Assert.Single(
                resource.Annotations.OfType<ContainerMountAnnotation>(),
                m => m.Type == ContainerMountType.Volume);
            Assert.StartsWith("pantrypress-", volume.Source, StringComparison.Ordinal);

            Assert.All(resource.Annotations.OfType<EndpointAnnotation>(), e => Assert.Null(e.Port));
        }
    }

    [Theory]
    [InlineData("sql-password")]
    [InlineData("redis-password")]
    public async Task Passwords_are_secret_parameters(string name)
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();

        var parameter = Assert.IsType<ParameterResource>(appHost.Resources.Single(r => r.Name == name));
        Assert.True(parameter.Secret);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Data_resources_start_healthy()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>(
            timeout.Token);

        // Run ephemeral: never attach to the developer's persistent containers or volumes, and leave nothing behind.
        foreach (var resource in appHost.Resources.Where(r => DataContainers.Contains(r.Name)))
        {
            // WithLifetime(Persistent) also adds PersistenceAnnotation, which DCP reads; removing only the
            // lifetime annotation still leaks a persistent container.
#pragma warning disable ASPIREPERSISTENCE001 // Evaluation API; a rename should break this build, not leak containers.
            RemoveAll(resource.Annotations, a => a is ContainerLifetimeAnnotation or PersistenceAnnotation);
#pragma warning restore ASPIREPERSISTENCE001
            RemoveAll(resource.Annotations, a => a is ContainerMountAnnotation { Type: ContainerMountType.Volume });
        }

        await using var app = await appHost.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);

        foreach (var name in HealthyResources)
        {
            await app.ResourceNotifications.WaitForResourceHealthyAsync(name, timeout.Token);
        }
    }

    private static void RemoveAll(ResourceAnnotationCollection annotations, Func<IResourceAnnotation, bool> match)
    {
        foreach (var annotation in annotations.Where(match).ToList())
        {
            annotations.Remove(annotation);
        }
    }
}
