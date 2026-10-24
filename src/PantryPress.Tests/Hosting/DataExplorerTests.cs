using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace PantryPress.Tests.Hosting;

// The developer's data explorers (DbGate for SQL, Redis Insight for Redis) run only in local Development (aspire.md).
public sealed class DataExplorerTests
{
    [Fact]
    public async Task Explorers_browse_their_own_data_resource_on_assigned_ports()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();

        foreach (var (name, parent) in EphemeralAppHost.DataExplorers)
        {
            var explorer = appHost.Resources.Single(r => r.Name == name);

            var relationship = Assert.Single(
                explorer.Annotations.OfType<ResourceRelationshipAnnotation>(),
                a => a.Type == "Parent");
            Assert.Equal(parent, relationship.Resource.Name);
            Assert.All(explorer.Annotations.OfType<EndpointAnnotation>(), e => Assert.Null(e.Port));
            Assert.DoesNotContain(
                explorer.Annotations,
                a => a is ContainerMountAnnotation { Type: ContainerMountType.Volume });
        }
    }

    [Fact]
    public async Task Publishing_has_no_explorers()
    {
        var outputPath = Directory.CreateTempSubdirectory("pantrypress-publish-").FullName;
        try
        {
            await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>(
                ["--operation", "publish", "--output-path", outputPath]);

            foreach (var (name, _) in EphemeralAppHost.DataExplorers)
            {
                Assert.DoesNotContain(appHost.Resources, r => r.Name == name);
            }
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }
}
