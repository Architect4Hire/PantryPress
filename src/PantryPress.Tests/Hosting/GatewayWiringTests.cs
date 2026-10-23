using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace PantryPress.Tests.Hosting;

// PP1.5: the gateway gets ApiService from the AppHost (aspire.md: no hard-coded endpoints), and the API's reference UI
// is linked from the dashboard in local Development only.
public sealed class GatewayWiringTests
{
    [Fact]
    public async Task The_gateway_references_the_api_without_waiting_for_it()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();

        var gateway = appHost.Resources.Single(r => r.Name == "gateway");

        Assert.Contains(
            gateway.Annotations.OfType<ResourceRelationshipAnnotation>(),
            r => r.Type == "Reference" && r.Resource.Name == "apiservice");

        // An API restart must not keep the admin app from loading; the gateway reports reachability instead.
        Assert.DoesNotContain(gateway.Annotations.OfType<WaitAnnotation>(), w => w.Resource.Name == "apiservice");
    }

    [Fact]
    public async Task The_api_reference_is_linked_in_local_development_only()
    {
        await using var development = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();

        Assert.Contains(
            development.Resources.Single(r => r.Name == "apiservice").Annotations.OfType<ResourceUrlsCallbackAnnotation>(),
            _ => true);

        var outputPath = Directory.CreateTempSubdirectory("pantrypress-publish-").FullName;
        try
        {
            await using var published = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>(
                ["--operation", "publish", "--output-path", outputPath]);

            Assert.DoesNotContain(
                published.Resources.Single(r => r.Name == "apiservice").Annotations.OfType<ResourceUrlsCallbackAnnotation>(),
                _ => true);
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }
}
