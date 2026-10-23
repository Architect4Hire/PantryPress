using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PantryPress.ApiService.Hosting;
using PantryPress.ServiceDefaults.EdgeHardening;

namespace PantryPress.Tests.Api;

// PP1.8 on the service a browser never reaches directly: the same response headers, a body ceiling, and a client
// address believed only when the gateway vouches for it (PSEC-002, backend.md).
public sealed class ApiEdgeHardeningTests
{
    private const string Register = "/api/v1/account/register";
    private const string SignedInRoute = "/api/v1/account/two-factor";

    [Fact]
    public async Task An_answer_carries_the_security_headers_and_claims_no_hsts()
    {
        // No HSTS: nothing reaches this service from a browser, and the hop that does reach it is plain HTTP inside
        // the environment by design.
        using var api = ApiServiceFactory.WithoutDatabase(signInWithTestHeader: false);

        using var response = await api.CreateClient().GetAsync(SignedInRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("nosniff", Header(response, SecurityHeaders.ContentTypeOptions));
        Assert.Equal("DENY", Header(response, SecurityHeaders.FrameOptions));
        Assert.Equal(ReferrerPolicies.Admin, Header(response, SecurityHeaders.ReferrerPolicy));
        Assert.Equal(PermissionsPolicies.DenyAll, Header(response, SecurityHeaders.PermissionsPolicy));
        Assert.Equal(ContentSecurityPolicies.Api, Header(response, SecurityHeaders.ContentSecurityPolicy));
        Assert.Null(Header(response, SecurityHeaders.StrictTransportSecurity));
    }

    [Fact]
    public async Task A_body_over_the_limit_is_refused_with_the_apis_own_problem()
    {
        using var api = ApiServiceFactory.WithoutDatabase();

        using var request = new HttpRequestMessage(HttpMethod.Post, Register)
        {
            Content = Body(RequestLimits.AdminDefault + 1),
        };
        using var response = await api.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("request-too-large", await response.CodeAsync());
    }

    [Fact]
    public async Task The_gateways_word_about_the_client_partitions_the_limit()
    {
        // One request a minute per address: with the gateway vouching, two browsers each get their own allowance.
        using var api = Limited();
        using var client = api.CreateClient();

        using var first = await RegisterAsync(client, "203.0.113.9", withSecret: true);
        using var second = await RegisterAsync(client, "198.51.100.7", withSecret: true);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task A_caller_without_the_secret_cannot_name_an_address_of_its_choosing()
    {
        // Otherwise a caller that can reach this service directly would be handed a fresh allowance per invented
        // address, which is no limit at all. Without the secret both land in the same bucket.
        using var api = Limited();
        using var client = api.CreateClient();

        using var first = await RegisterAsync(client, "203.0.113.9", withSecret: false);
        using var second = await RegisterAsync(client, "198.51.100.7", withSecret: false);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("too-many-requests", await second.CodeAsync());
    }

    private static ApiServiceFactory Limited() =>
        ApiServiceFactory.WithoutDatabase(
            extraSettings: new Dictionary<string, string?> { ["RateLimits:AnonymousAccountPerIpPerMinute"] = "1" });

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, string address, bool withSecret)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Register)
        {
            Content = JsonContent.Create(new { email = $"cook@{address}.test", password = "a long kitchen phrase" }),
        };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", address);
        if (withSecret)
        {
            request.Headers.TryAddWithoutValidation(GatewaySession.SecretHeader, TestSecrets.GatewaySecret);
        }

        return await client.SendAsync(request);
    }

    private static StringContent Body(long bytes)
    {
        var content = new StringContent(new string('x', (int)bytes));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
}
