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
    public async Task The_gateway_and_the_api_share_one_generated_secret_and_nobody_else_holds_it()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();

        var holders = new Dictionary<string, ParameterResource>(StringComparer.Ordinal);
        foreach (var resource in appHost.Resources.OfType<IResourceWithEnvironment>())
        {
            var variables = await GetEnvironmentAsync(resource, appHost.ExecutionContext);
            if (variables.TryGetValue("Gateway__SharedSecret", out var secret))
            {
                holders[resource.Name] = Assert.IsType<ParameterResource>(secret);
            }
        }

        // Exactly these two, and the very same parameter: the secret is how the API tells the gateway from anyone
        // else that can reach it (PP1.6).
        Assert.Equal(["apiservice", "gateway"], holders.Keys.Order(StringComparer.Ordinal));
        Assert.Single(holders.Values.Distinct());
        var parameter = holders.Values.First();
        Assert.Equal("gateway-identity-secret", parameter.Name);
        Assert.True(parameter.Secret);
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

    private static async Task<Dictionary<string, object>> GetEnvironmentAsync(
        IResourceWithEnvironment resource,
        DistributedApplicationExecutionContext executionContext)
    {
        var context = new EnvironmentCallbackContext(executionContext, resource);
        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        return context.EnvironmentVariables;
    }
}
