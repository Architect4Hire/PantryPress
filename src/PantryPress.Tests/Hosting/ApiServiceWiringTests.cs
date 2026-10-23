using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace PantryPress.Tests.Hosting;

// PP1.2b and PP1.2c: ApiService gets the database and the admin app origin for email links from the AppHost, plus the
// mail sink in local Development only (aspire.md: no hard-coded endpoints).
public sealed class ApiServiceWiringTests
{
    [Fact]
    public async Task ApiService_gets_the_database_mail_sink_and_link_origin_from_the_AppHost()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();
        var apiService = (IResourceWithEnvironment)appHost.Resources.Single(r => r.Name == "apiservice");

        var variables = await GetEnvironmentAsync(apiService, appHost.ExecutionContext);

        Assert.Contains("ConnectionStrings__pantrypress", variables.Keys);
        Assert.Contains("DevelopmentMail__Host", variables.Keys);
        Assert.Contains("DevelopmentMail__Port", variables.Keys);
        var origin = Assert.IsType<ReferenceExpression>(variables["Accounts__AppBaseUrl"]);
        Assert.StartsWith("https://app.localhost:", origin.Format, StringComparison.Ordinal);
        Assert.Contains(
            origin.ValueProviders.OfType<EndpointReferenceExpression>(),
            e => e.Endpoint.Resource.Name == "gateway" && e.Endpoint.EndpointName == "https");
    }

    [Fact]
    public async Task Published_ApiService_takes_the_link_origin_from_a_parameter_and_has_no_mail_sink()
    {
        var outputPath = Directory.CreateTempSubdirectory("pantrypress-publish-").FullName;
        try
        {
            await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>(
                ["--operation", "publish", "--output-path", outputPath]);
            var apiService = (IResourceWithEnvironment)appHost.Resources.Single(r => r.Name == "apiservice");

            var variables = await GetEnvironmentAsync(apiService, appHost.ExecutionContext);

            var origin = Assert.IsType<ParameterResource>(variables["Accounts__AppBaseUrl"]);
            Assert.Equal("app-base-url", origin.Name);
            Assert.DoesNotContain(variables.Keys, k => k.StartsWith("DevelopmentMail__", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    public async Task Gateway_has_the_https_endpoint_the_email_links_use()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();

        var gateway = appHost.Resources.Single(r => r.Name == "gateway");

        Assert.Contains(gateway.Annotations.OfType<EndpointAnnotation>(), e => e.Name == "https" && e.UriScheme == "https");
    }

    private static async Task<Dictionary<string, object>> GetEnvironmentAsync(
        IResourceWithEnvironment resource,
        DistributedApplicationExecutionContext executionContext)
    {
        var context = new EnvironmentCallbackContext(executionContext);
        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        return context.EnvironmentVariables;
    }
}
