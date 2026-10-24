using System.Net;
using System.Net.Http.Json;
using PantryPress.Tests.Api;

namespace PantryPress.Tests.Gateway;

// PP1.12 (audit finding): the two halves of the per-address limit on the anonymous account routes, joined. PP1.8 proved
// the gateway forwards the browser's address and, separately, that ApiService partitions by an address the gateway
// vouches for - but the gateway vouched only for signed-in requests, so sign-up and password reset shared one bucket
// keyed by the gateway's own address. Here what the real gateway forwards for each browser is replayed, header for
// header, into the real ApiService.
public sealed class GatewayToApiLimitTests
{
    private const string Register = "/api/v1/account/register";

    [Fact]
    public async Task Two_browsers_behind_the_gateway_each_get_their_own_allowance_at_the_api()
    {
        await using var gateway = new GatewayFactory("Development");
        using var api = ApiServiceFactory.WithoutDatabase(
            extraSettings: new Dictionary<string, string?> { ["RateLimits:AnonymousAccountPerIpPerMinute"] = "1" },
            signInWithTestHeader: false);
        using var apiClient = api.CreateClient();

        var first = await ForwardedAsync(gateway, "203.0.113.9");
        var second = await ForwardedAsync(gateway, "198.51.100.7");
        var firstAgain = await ForwardedAsync(gateway, "203.0.113.9");

        using var firstAnswer = await ReplayAsync(apiClient, first);
        using var secondAnswer = await ReplayAsync(apiClient, second);
        using var firstAgainAnswer = await ReplayAsync(apiClient, firstAgain);

        // Each browser has its own allowance of one; the first browser's second attempt is the one refused.
        Assert.NotEqual(HttpStatusCode.TooManyRequests, firstAnswer.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, secondAnswer.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, firstAgainAnswer.StatusCode);
    }

    // Sends a sign-up through the gateway as a browser at the address, and returns what the gateway forwarded.
    private static async Task<GatewayFactory.ForwardedRequest> ForwardedAsync(GatewayFactory gateway, string address)
    {
        var before = gateway.Forwarded.Count;
        using var browser = gateway.CreateBrowser();
        browser.DefaultRequestHeaders.Add(GatewayFactory.RemoteAddressHeader, address);
        using var response = await browser.PostAsJsonAsync(Register, new { email = "cook@example.test", password = "a long kitchen phrase" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return gateway.Forwarded.Skip(before).Single(r => r.Uri.AbsolutePath == Register);
    }

    private static Task<HttpResponseMessage> ReplayAsync(HttpClient api, GatewayFactory.ForwardedRequest forwarded)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Register)
        {
            Content = JsonContent.Create(new { email = "cook@example.test", password = "a long kitchen phrase" }),
        };

        // Exactly the gateway's own voice: its secret and the forwarding headers it set, nothing a test made up.
        foreach (var (name, value) in forwarded.Headers.Where(h =>
            h.Key.StartsWith("X-PP-", StringComparison.OrdinalIgnoreCase)
            || h.Key.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        Assert.NotNull(forwarded.Header("X-Forwarded-For"));
        return api.SendAsync(request);
    }
}
