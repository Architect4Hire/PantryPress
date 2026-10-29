using System.Net;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using PantryPress.ApiService.Http;
using PantryPress.Gateway.Hosting;
using PantryPress.Tests.Api;
using Yarp.ReverseProxy.Configuration;

namespace PantryPress.Tests.Gateway;

// PP1.5: the gateway forwards the admin API surface to ApiService and owns everything else under /api (ACCT-002,
// gateway.md, api-contract.md). The session, header hygiene, and antiforgery arrive with PP1.6 and PP1.7.
public sealed class GatewayRoutingTests
{
    [Theory]
    [InlineData("/api/v1/account/two-factor")]
    [InlineData("/api/v1/account/two-factor/recovery-codes")]
    [InlineData("/api/v1/sites/market/posts?limit=20&cursor=abc")]
    public async Task Admin_api_paths_reach_the_api_service_unchanged(string path)
    {
        await using var gateway = new GatewayFactory("Development");

        using var response = await gateway.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("forwarded", await response.Content.ReadAsStringAsync());
        var forwarded = Assert.Single(gateway.Forwarded);
        Assert.Equal(GatewayFactory.ApiDestination, forwarded.Uri.Host);
        Assert.Equal(path, forwarded.Uri.PathAndQuery);
        Assert.Equal("GET", forwarded.Method);
    }

    [Fact]
    public async Task A_write_keeps_its_method_and_body()
    {
        await using var gateway = new GatewayFactory("Development");

        using var response = await gateway.CreateBrowser()
            .PostAsJsonAsync("/api/v1/account/register", new { email = "cook@example.test" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = Assert.Single(gateway.Forwarded);
        Assert.Equal("POST", forwarded.Method);
        Assert.Equal("/api/v1/account/register", forwarded.Uri.PathAndQuery);
    }

    [Theory]
    [InlineData("/api/publishing/v1/posts")] // its own edge route, never the BFF (gateway.md)
    [InlineData("/api/internal/v1/sessions/credentials")] // the gateway's own call to the API, never a browser's (PP1.6)
    [InlineData("/api/public/v1/pages/home")] // the public site API answers on site hosts
    [InlineData("/api/v2/account")] // no such version
    [InlineData("/api/account")] // unversioned
    [InlineData("/api")]
    public async Task Every_other_api_path_gets_a_problem_404_and_is_never_forwarded(string path)
    {
        await using var gateway = new GatewayFactory("Development");

        using var response = await gateway.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(GatewayApi.NotFoundCode, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(GatewayApi.NotFoundTitle, problem.RootElement.GetProperty("title").GetString());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
        Assert.Empty(gateway.Forwarded);
    }

    [Fact]
    public async Task The_bare_version_prefix_goes_to_the_api_which_answers_for_it()
    {
        await using var gateway = new GatewayFactory("Development");

        using var response = await gateway.CreateClient().GetAsync("/api/v1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GatewayFactory.ApiDestination, Assert.Single(gateway.Forwarded).Uri.Host);
    }

    [Fact]
    public async Task The_gateway_and_the_api_refuse_an_unknown_path_the_same_way()
    {
        await using var gateway = new GatewayFactory("Development");
        await using var api = ApiServiceFactory.WithoutDatabase();

        using var fromGateway = await gateway.CreateClient().GetAsync("/api/nothing-here");
        using var fromApi = await api.CreateClient().GetAsync("/api/v1/nothing-here");

        using var gatewayProblem = JsonDocument.Parse(await fromGateway.Content.ReadAsStringAsync());
        using var apiProblem = JsonDocument.Parse(await fromApi.Content.ReadAsStringAsync());
        Assert.Equal(fromApi.StatusCode, fromGateway.StatusCode);
        Assert.Equal(fromApi.Content.Headers.ContentType?.MediaType, fromGateway.Content.Headers.ContentType?.MediaType);

        // The same fields with the same values: the admin app reads one shape whoever answered.
        Assert.Equal(Fields(apiProblem), Fields(gatewayProblem));
        foreach (var field in new[] { ApiProblems.CodeKey, "title", "type" })
        {
            Assert.Equal(
                apiProblem.RootElement.GetProperty(field).GetString(),
                gatewayProblem.RootElement.GetProperty(field).GetString());
        }

        Assert.Equal(
            apiProblem.RootElement.GetProperty("status").GetInt32(),
            gatewayProblem.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task A_forward_that_never_reaches_the_api_still_answers_with_a_problem()
    {
        // YARP answers a failed forward with a bodiless 502; without the gateway's problem details the admin app would
        // have nothing to show exactly when the API is down.
        await using var gateway = new GatewayFactory(
            "Development",
            forward: () => throw new HttpRequestException("Connection refused."));

        using var response = await gateway.CreateClient().GetAsync("/api/v1/account/two-factor");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(GatewayApi.ServerErrorCode, problem.RootElement.GetProperty(ApiProblems.CodeKey).GetString());
        Assert.Equal(GatewayApi.ServerErrorTitle, problem.RootElement.GetProperty("title").GetString());

        // Nothing about the failure reaches the caller.
        var raw = problem.RootElement.GetRawText();
        Assert.DoesNotContain("refused", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_only_forwarded_prefix_is_the_admin_surface()
    {
        // ApiService declares the surfaces; the gateway's route table has to agree, or a new surface would answer 404
        // at the BFF while every API test passed.
        await using var gateway = new GatewayFactory("Development");

        var routes = gateway.Services.GetRequiredService<IProxyConfigProvider>().GetConfig().Routes;

        var forwarded = routes.Where(r => r.ClusterId == "api").Select(r => r.Match.Path).ToList();
        Assert.Equal([$"/{ApiSurfaces.AdminV1Route}/{{**rest}}"], forwarded);
        Assert.DoesNotContain(routes, r => r.Match.Path?.StartsWith("/api/publishing", StringComparison.Ordinal) == true);
    }

    private static List<string> Fields(JsonDocument problem) =>
        [.. problem.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];

    [Theory]
    [InlineData("/")]
    [InlineData("/posts/new")]
    public async Task Development_sends_everything_else_to_the_admin_app(string path)
    {
        await using var gateway = new GatewayFactory("Development");

        using var response = await gateway.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = Assert.Single(gateway.Forwarded);
        Assert.Equal(GatewayFactory.AdminDestination, forwarded.Uri.Host);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/posts/new")]
    public async Task Outside_Development_nothing_reaches_the_dev_server(string path)
    {
        // The catch-all route lives in appsettings.Development.json; elsewhere the gateway serves the build (PPL.3).
        await using var gateway = new GatewayFactory("Production");

        using var response = await gateway.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(gateway.Forwarded);
    }

    [Fact]
    public async Task Outside_Development_the_admin_api_is_still_routed()
    {
        await using var gateway = new GatewayFactory("Production");

        using var response = await gateway.CreateClient().GetAsync("/api/v1/account/two-factor");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GatewayFactory.ApiDestination, Assert.Single(gateway.Forwarded).Uri.Host);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task The_gateways_own_endpoints_are_not_shadowed_by_a_route(string path)
    {
        await using var gateway = new GatewayFactory("Development");

        using var response = await gateway.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(gateway.Forwarded);
    }
}
