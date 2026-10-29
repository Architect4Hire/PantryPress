using System.Net;
using System.Net.Http.Headers;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using PantryPress.Gateway.Hosting;
using PantryPress.ServiceDefaults.EdgeHardening;
using PantryPress.Tests.Persistence;

namespace PantryPress.Tests.Gateway;

// PP1.8 against the real thing: a real Kestrel, real TLS, and the real pipeline. The in-memory tests prove what each
// middleware decides; these prove the server the decisions are made on behaves the same way.
[Collection(MigratedDatabase.CollectionName)]
public sealed class EdgeHardeningEndToEndTests(MigratedDatabase system)
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_real_gateway_hardens_its_answers()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await system.App.ResourceNotifications.WaitForResourceHealthyAsync("gateway", timeout.Token);

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false,
        };
        using var secure = new HttpClient(handler) { BaseAddress = system.App.GetEndpoint("gateway", "https") };
        var host = $"localhost:{secure.BaseAddress!.Port}";

        using var answer = await SendAsync(secure, host, HttpMethod.Get, "/api/v1/account/session", timeout.Token);
        Assert.Equal("nosniff", Header(answer, SecurityHeaders.ContentTypeOptions));
        Assert.Equal("DENY", Header(answer, SecurityHeaders.FrameOptions));
        Assert.Equal(ReferrerPolicies.Admin, Header(answer, SecurityHeaders.ReferrerPolicy));
        Assert.Equal(ContentSecurityPolicies.Api, Header(answer, SecurityHeaders.ContentSecurityPolicy));

        // Development, so no browser running against a local host is pinned to HTTPS for a year.
        Assert.Null(Header(answer, SecurityHeaders.StrictTransportSecurity));

        // Real Kestrel, a real declared length: refused before the body is read, and with the API's problem shape.
        using var oversized = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/account/register", UriKind.Relative))
        {
            Content = new StringContent(new string('x', (int)RequestLimits.AdminDefault + 1)),
            Headers = { Host = host },
        };
        oversized.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        // The client waits for the gateway before sending the body. Without that it races the refusal: the gateway answers
        // and closes while the client is still writing, and the client can see the reset instead of the answer. With
        // it, getting the 413 at all proves the body was refused unread.
        oversized.Headers.ExpectContinue = true;
        using var refused = await secure.SendAsync(oversized, timeout.Token);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, refused.StatusCode);
        Assert.Equal(GatewayApi.TooLargeCode, await refused.CodeAsync());

        // A host that is not the gateway's gets the neutral 404, whatever it asks for: a blog's, and since PP-D19 the
        // app. host the platform home used to answer on (PPL.1), in the configuration this host really runs with.
        foreach (var other in new[] { "cook.localhost", "app.localhost", "www.localhost" })
        {
            using var stranger = await SendAsync(secure, other, HttpMethod.Get, "/api/v1/account/session", timeout.Token);
            Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
            Assert.Equal(GatewayApi.NotFoundCode, await stranger.CodeAsync());
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_real_gateway_sends_plain_http_to_tls_but_answers_a_probe()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await system.App.ResourceNotifications.WaitForResourceHealthyAsync("gateway", timeout.Token);

        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var plain = new HttpClient(handler) { BaseAddress = system.App.GetEndpoint("gateway", "http") };
        var host = $"localhost:{plain.BaseAddress!.Port}";

        using var redirected = await SendAsync(plain, host, HttpMethod.Get, "/api/v1/account/session", timeout.Token);
        Assert.Equal(HttpStatusCode.TemporaryRedirect, redirected.StatusCode);
        Assert.Equal(Uri.UriSchemeHttps, redirected.Headers.Location?.Scheme);

        // The probe Container Apps makes over plain HTTP still answers, which a redirect would have broken.
        using var probe = await plain.GetAsync(new Uri("/health", UriKind.Relative), timeout.Token);
        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string host,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Host = host;
        return await client.SendAsync(request, cancellationToken);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
}
