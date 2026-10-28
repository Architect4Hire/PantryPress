using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace PantryPress.Tests.Hosting;

// PP1.2b and PP1.2c: the database and the admin app origin for email links come from the AppHost, plus the mail sink in
// local Development only (aspire.md: no hard-coded endpoints). Since PP1.10b the Worker sends account email from the
// outbox, so the origin and the sink are the Worker's, and ApiService has neither.
public sealed class ApiServiceWiringTests
{
    [Fact]
    public async Task The_worker_gets_the_mail_sink_and_link_origin_and_ApiService_the_database_alone()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();
        var apiService = (IResourceWithEnvironment)appHost.Resources.Single(r => r.Name == "apiservice");
        var worker = (IResourceWithEnvironment)appHost.Resources.Single(r => r.Name == "worker");

        var api = await GetEnvironmentAsync(apiService, appHost.ExecutionContext);
        var variables = await GetEnvironmentAsync(worker, appHost.ExecutionContext);

        Assert.Contains("ConnectionStrings__pantrypress", api.Keys);
        Assert.DoesNotContain(api.Keys, k => k.StartsWith("DevelopmentMail__", StringComparison.Ordinal));
        Assert.DoesNotContain("Accounts__AppBaseUrl", api.Keys);
        Assert.DoesNotContain("Sites__AppBaseUrl", api.Keys);

        // A local run's sites answer at {slug}.localhost (SITE-009; PP2.10).
        Assert.Equal("localhost", api["Sites__SubdomainZone"]);
        Assert.Contains("ConnectionStrings__pantrypress", variables.Keys);
        Assert.Contains("DevelopmentMail__Host", variables.Keys);
        Assert.Contains("DevelopmentMail__Port", variables.Keys);
        var origin = Assert.IsType<ReferenceExpression>(variables["Accounts__AppBaseUrl"]);
        Assert.StartsWith("https://app.localhost:", origin.Format, StringComparison.Ordinal);
        Assert.Contains(
            origin.ValueProviders.OfType<EndpointReferenceExpression>(),
            e => e.Endpoint.Resource.Name == "gateway" && e.Endpoint.EndpointName == "https");

        // Invitation links (PP2.11) open the same origin, read from the same key (PP2.14).
        Assert.DoesNotContain("Sites__AppBaseUrl", variables.Keys);
    }

    [Fact]
    public async Task The_published_worker_takes_the_link_origin_from_a_parameter_and_has_no_mail_sink()
    {
        var outputPath = Directory.CreateTempSubdirectory("pantrypress-publish-").FullName;
        try
        {
            await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>(
                ["--operation", "publish", "--output-path", outputPath]);
            var worker = (IResourceWithEnvironment)appHost.Resources.Single(r => r.Name == "worker");

            var variables = await GetEnvironmentAsync(worker, appHost.ExecutionContext);

            var origin = Assert.IsType<ParameterResource>(variables["Accounts__AppBaseUrl"]);
            Assert.Equal("app-base-url", origin.Name);
            Assert.DoesNotContain("Sites__AppBaseUrl", variables.Keys);
            Assert.DoesNotContain(variables.Keys, k => k.StartsWith("DevelopmentMail__", StringComparison.Ordinal));

            // Deployed, the zone new sites' addresses sit under is the environment's own (PP2.10).
            var apiService = (IResourceWithEnvironment)appHost.Resources.Single(r => r.Name == "apiservice");
            var zone = Assert.IsType<ParameterResource>((await GetEnvironmentAsync(apiService, appHost.ExecutionContext))["Sites__SubdomainZone"]);
            Assert.Equal("site-subdomain-zone", zone.Name);
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    public async Task The_public_site_reads_the_database_and_redis_and_nothing_of_the_gateways()
    {
        // PP2.12: the site resolves each request's host through the host map in Redis, reading SiteHosts on a miss; the API
        // refreshes the map when it creates a site. Both wait for Redis; the site takes no gateway secret and no zone,
        // because it never creates a site or trusts a gateway.
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();
        var siteResource = (IResourceWithEnvironment)appHost.Resources.Single(r => r.Name == "site");
        var apiResource = (IResourceWithEnvironment)appHost.Resources.Single(r => r.Name == "apiservice");

        var site = await GetEnvironmentAsync(siteResource, appHost.ExecutionContext);
        var api = await GetEnvironmentAsync(apiResource, appHost.ExecutionContext);

        Assert.Contains("ConnectionStrings__pantrypress", site.Keys);
        Assert.Contains("ConnectionStrings__redis", site.Keys);
        Assert.Contains("ConnectionStrings__redis", api.Keys);
        Assert.DoesNotContain("Gateway__SharedSecret", site.Keys);
        Assert.DoesNotContain("Sites__SubdomainZone", site.Keys);
        Assert.Contains(siteResource.Annotations.OfType<WaitAnnotation>(), w => w.Resource.Name == "redis");
        Assert.Contains(apiResource.Annotations.OfType<WaitAnnotation>(), w => w.Resource.Name == "redis");
        Assert.Contains(
            siteResource.Annotations.OfType<WaitAnnotation>(),
            w => w.Resource.Name == "migrationservice" && w.WaitType == WaitType.WaitForCompletion);
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
