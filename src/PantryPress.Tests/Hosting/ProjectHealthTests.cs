using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace PantryPress.Tests.Hosting;

// PP0.5: every long-running project reports readiness on /health and liveness on /alive; the one-shot migration
// service has no endpoints (aspire.md).
public sealed class ProjectHealthTests
{
    private static readonly string[] LongRunningProjects = ["apiservice", "site", "worker", "gateway"];

    [Fact]
    public async Task Long_running_projects_have_an_http_health_check()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();

        foreach (var name in LongRunningProjects)
        {
            var resource = appHost.Resources.Single(r => r.Name == name);
            Assert.Single(resource.Annotations.OfType<HealthCheckAnnotation>());
        }
    }

    [Fact]
    public async Task Migration_service_has_no_endpoints_or_health_check()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();

        var resource = appHost.Resources.Single(r => r.Name == "migrationservice");

        Assert.Empty(resource.Annotations.OfType<EndpointAnnotation>());
        Assert.Empty(resource.Annotations.OfType<HealthCheckAnnotation>());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Long_running_projects_start_healthy_and_alive()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var appHost = await EphemeralAppHost.CreateAsync(timeout.Token);
        await using var app = await appHost.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);

        foreach (var name in LongRunningProjects)
        {
            await app.ResourceNotifications.WaitForResourceHealthyAsync(name, timeout.Token);

            using var client = app.CreateHttpClient(name, "http");
            using var alive = await client.GetAsync(new Uri("/alive", UriKind.Relative), timeout.Token);
            Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
        }
    }
}
