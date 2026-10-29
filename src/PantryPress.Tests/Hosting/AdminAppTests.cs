using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace PantryPress.Tests.Hosting;

// PP0.7: the admin SPA's dev server runs under Aspire in local Development, and browsers are meant to reach it through
// the gateway's YARP route (PP-D08, gateway.md). Publishing never produces an admin resource.
public sealed class AdminAppTests
{
    [Fact]
    public async Task Admin_dev_server_has_an_assigned_port_and_a_health_check()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();

        var resource = appHost.Resources.Single(r => r.Name == "admin");

        Assert.Single(resource.Annotations.OfType<HealthCheckAnnotation>());
        var endpoint = Assert.Single(resource.Annotations.OfType<EndpointAnnotation>());
        Assert.Null(endpoint.Port);
    }

    [Fact]
    public async Task Gateway_references_the_admin_dev_server_without_waiting_for_it()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();

        var gateway = appHost.Resources.Single(r => r.Name == "gateway");

        Assert.Contains(
            gateway.Annotations.OfType<ResourceRelationshipAnnotation>(),
            r => r.Type == "Reference" && r.Resource.Name == "admin");
        Assert.DoesNotContain(gateway.Annotations.OfType<WaitAnnotation>(), w => w.Resource.Name == "admin");
    }

    [Fact]
    public async Task Publishing_has_no_admin_resource()
    {
        var outputPath = Directory.CreateTempSubdirectory("pantrypress-publish-").FullName;
        try
        {
            await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>(
                ["--operation", "publish", "--output-path", outputPath]);

            Assert.True(appHost.ExecutionContext.IsPublishMode);
            Assert.DoesNotContain(appHost.Resources, r => r.Name.StartsWith("admin", StringComparison.Ordinal));
            var gateway = appHost.Resources.Single(r => r.Name == "gateway");
            Assert.DoesNotContain(
                gateway.Annotations.OfType<ResourceRelationshipAnnotation>(),
                r => r.Resource.Name == "admin");
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Gateway_serves_the_admin_app_and_keeps_its_own_health_endpoint()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var appHost = await EphemeralAppHost.CreateAsync(timeout.Token);
        await using var app = await appHost.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);

        await app.ResourceNotifications.WaitForResourceHealthyAsync("gateway", timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("admin", timeout.Token);
        // Over TLS: since PP1.8 the gateway sends a plain-HTTP caller to HTTPS. The certificate is not checked: what is
        // under test is the gateway's answer, not whether this machine trusts the development certificate.
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var client = new HttpClient(handler) { BaseAddress = app.GetEndpoint("gateway", "https") };

        // Browsers send the host the dashboard links to: the local apex, localhost (PP-D19).
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/", UriKind.Relative));
        request.Headers.Host = $"localhost:{client.BaseAddress!.Port}";
        using var page = await client.SendAsync(request, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        // '/' is a public path, so the dev server renders it as the build prerenders it (PPL.2): the page arrives with
        // its heading, not an empty root. A document is never kept without asking (PPL.3).
        var html = await page.Content.ReadAsStringAsync(timeout.Token);
        Assert.Contains("<ppa-root", html, StringComparison.Ordinal);
        Assert.Contains("<h1>PantryPress</h1>", html, StringComparison.Ordinal);
        Assert.True(page.Headers.CacheControl?.NoCache, page.Headers.CacheControl?.ToString());

        // The catch-all SPA route must not shadow the gateway's own endpoints.
        using var health = await client.GetAsync(new Uri("/health", UriKind.Relative), timeout.Token);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Healthy", await health.Content.ReadAsStringAsync(timeout.Token));
    }
}
