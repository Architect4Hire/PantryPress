using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace PantryPress.Tests.Hosting;

// PP0.5: ServiceDefaults /health and /alive (aspire.md). Liveness never calls SQL, Redis, blob, or providers.
public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Alive_and_health_are_healthy_with_no_dependency_checks()
    {
        await using var app = await StartAsync(Environments.Development);
        using var client = app.GetTestClient();

        await AssertStatusAsync(client, "/alive", HttpStatusCode.OK, "Healthy");
        await AssertStatusAsync(client, "/health", HttpStatusCode.OK, "Healthy");
    }

    [Fact]
    public async Task Alive_never_runs_a_dependency_check()
    {
        var dependency = new CountingCheck();
        await using var app = await StartAsync(Environments.Development, checks => checks.AddCheck("sql", dependency));
        using var client = app.GetTestClient();

        await AssertStatusAsync(client, "/alive", HttpStatusCode.OK, "Healthy");

        Assert.Equal(0, dependency.Calls);
    }

    [Fact]
    public async Task Health_runs_dependency_checks_without_revealing_them()
    {
        var dependency = new CountingCheck();
        await using var app = await StartAsync(Environments.Development, checks => checks.AddCheck("sql", dependency));
        using var client = app.GetTestClient();

        await AssertStatusAsync(client, "/health", HttpStatusCode.ServiceUnavailable, "Unhealthy");

        Assert.Equal(1, dependency.Calls);
    }

    [Theory]
    [InlineData("/alive")]
    [InlineData("/health")]
    public async Task Probes_run_before_any_later_middleware(string path)
    {
        // Stands in for Host resolution, sessions, or rate limits backed by SQL or Redis.
        await using var app = await StartAsync(
            Environments.Development,
            configure: app => app.Use((HttpContext _, RequestDelegate _) =>
                throw new InvalidOperationException("Middleware ran for a probe.")));
        using var client = app.GetTestClient();

        await AssertStatusAsync(client, path, HttpStatusCode.OK, "Healthy");
    }

    [Fact]
    public async Task Startup_fails_when_a_dependency_check_is_tagged_for_liveness()
    {
        var builder = CreateBuilder(Environments.Development);
        builder.Services.AddHealthChecks().AddCheck("redis", new CountingCheck(), tags: ["live"]);
        await using var app = builder.Build();

        var error = Assert.Throws<InvalidOperationException>(() => app.MapDefaultEndpoints());

        Assert.Contains("redis", error.Message, StringComparison.Ordinal);
    }

    // Deliberate until PP19.2 decides production probes and keeps them off public ingress (decisions record, section 3).
    [Theory]
    [InlineData("/alive")]
    [InlineData("/health")]
    public async Task Endpoints_stay_development_only_until_production_probes_are_decided(string path)
    {
        await using var app = await StartAsync(Environments.Production);
        using var client = app.GetTestClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplicationBuilder CreateBuilder(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.AddServiceDefaults();
        return builder;
    }

    private static async Task<WebApplication> StartAsync(
        string environment,
        Action<IHealthChecksBuilder>? addChecks = null,
        Action<WebApplication>? configure = null)
    {
        var builder = CreateBuilder(environment);
        addChecks?.Invoke(builder.Services.AddHealthChecks());

        var app = builder.Build();
        app.MapDefaultEndpoints();
        configure?.Invoke(app);
        await app.StartAsync();
        return app;
    }

    private static async Task AssertStatusAsync(HttpClient client, string path, HttpStatusCode status, string body)
    {
        var response = await client.GetAsync(path);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(body, await response.Content.ReadAsStringAsync());
    }

    // Stands in for a SQL, Redis, blob, or provider check: always unhealthy, and counts how often it runs.
    private sealed class CountingCheck : IHealthCheck
    {
        private int _calls;

        public int Calls => _calls;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(HealthCheckResult.Unhealthy());
        }
    }
}
