using System.Net;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using PantryPress.ApiService.Http;
using PantryPress.Gateway.Hosting;
using PantryPress.Tests.Persistence;

namespace PantryPress.Tests.Gateway;

// PP1.5 end to end: the real gateway at the apex, localhost, reaches the real ApiService, reports it reachable, and the API's
// documentation is readable in a local run (ACCT-002, gateway.md, API-009's local half). It shares the running system
// with the other integration tests rather than starting its own: several AppHosts at once outrun what a developer
// machine can start inside the timeout (see MigratedDatabase).
[Collection(MigratedDatabase.CollectionName)]
public sealed class GatewayApiIntegrationTests(MigratedDatabase system)
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_admin_api_answers_through_the_gateway()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await system.App.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", timeout.Token);
        await system.App.ResourceNotifications.WaitForResourceHealthyAsync("gateway", timeout.Token);
        // Over TLS: since PP1.8 the gateway sends a plain-HTTP caller to HTTPS, so the http endpoint answers 307 and
        // nothing else. The certificate is not checked: what is under test is the gateway's answer, not whether this
        // machine trusts the development certificate (SessionEndToEndTests says the same).
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var client = new HttpClient(handler) { BaseAddress = system.App.GetEndpoint("gateway", "https") };
        var host = $"localhost:{client.BaseAddress!.Port}";

        // A signed-in route: the API's own 401 problem proves the request crossed the gateway into ApiService.
        using var signedIn = await SendAsync(client, host, "/api/v1/account/two-factor", timeout.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, signedIn.StatusCode);
        Assert.Equal("sign-in-required", await CodeAsync(signedIn, timeout.Token));

        // The publishing API has its own edge route; through the BFF it does not exist.
        using var publishing = await SendAsync(client, host, "/api/publishing/v1/posts", timeout.Token);
        Assert.Equal(HttpStatusCode.NotFound, publishing.StatusCode);
        Assert.Equal(GatewayApi.NotFoundCode, await CodeAsync(publishing, timeout.Token));

        // With the API answering, the gateway reports itself healthy rather than degraded (gateway.md).
        using var health = await client.GetAsync(new Uri("/health", UriKind.Relative), timeout.Token);
        Assert.Equal("Healthy", await health.Content.ReadAsStringAsync(timeout.Token));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_api_serves_its_documentation_in_a_local_run()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await system.App.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", timeout.Token);
        using var client = system.App.CreateHttpClient("apiservice", "http");

        using var document = await client.GetAsync(
            new Uri($"/openapi/{ApiSurfaces.AdminV1Document}.json", UriKind.Relative),
            timeout.Token);
        using var reference = await client.GetAsync(
            new Uri($"/scalar/{ApiSurfaces.AdminV1Document}", UriKind.Relative),
            timeout.Token);

        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reference.StatusCode);
        Assert.Equal("text/html", reference.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string host,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Host = host;
        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(ApiProblems.ContentType, response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return problem.RootElement.GetProperty(ApiProblems.CodeKey).GetString();
    }
}
