using PantryPress.ServiceDefaults.EdgeHardening;

namespace PantryPress.Tests.Gateway;

// PP1.7: what a client claims about the hops in front of the gateway reaches nothing (ACCT-002, gateway.md). PP1.8
// added the trusted-proxy step ahead of the strip, and it trusts nobody unless an operator configured a peer, so a
// forwarding header from a client is still a client's invention and ApiService never reads one as a proxy's word.
public sealed class ForwardedHeaderTests
{
    private const string AdminRoute = "/api/v1/account/two-factor";

    [Theory]
    [InlineData("Forwarded", "for=203.0.113.9;host=cook.pantrypress.app;proto=https")]
    [InlineData("X-Forwarded-For", "203.0.113.9")]
    [InlineData("X-Forwarded-Proto", "http")]
    [InlineData("X-Forwarded-Scheme", "http")]
    [InlineData("X-Forwarded-Host", "cook.pantrypress.app")]
    [InlineData("X-Forwarded-Port", "8080")]
    [InlineData("X-Forwarded-Prefix", "/elsewhere")]
    public async Task A_clients_forwarding_header_never_reaches_the_api(string header, string value)
    {
        await using var gateway = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());
        using var browser = await gateway.SignedInAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, AdminRoute);
        request.Headers.TryAddWithoutValidation(header, value);
        using var response = await browser.SendAsync(request);

        // The gateway may set a header of its own with the same name; what must not survive is the client's value.
        var forwarded = Assert.Single(gateway.Forwarded);
        Assert.NotEqual(value, forwarded.Header(header));
    }

    [Fact]
    public async Task The_whole_list_goes_at_once_even_unsigned_in()
    {
        await using var gateway = new GatewayFactory("Development");

        using var request = new HttpRequestMessage(HttpMethod.Get, AdminRoute);
        foreach (var header in TrustedProxies.Forwarding)
        {
            request.Headers.TryAddWithoutValidation(header, "invented");
        }

        using var response = await gateway.CreateBrowser().SendAsync(request);

        var forwarded = Assert.Single(gateway.Forwarded);
        foreach (var header in TrustedProxies.Forwarding)
        {
            Assert.DoesNotContain("invented", forwarded.Header(header) ?? string.Empty, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_api_is_told_what_the_gateway_itself_can_see_and_only_that()
    {
        // Set, not append: if the gateway appended, a client's claim would arrive as the first value of a list the API
        // had every reason to trust.
        await using var gateway = new GatewayFactory("Development");

        using var request = new HttpRequestMessage(HttpMethod.Get, AdminRoute);
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.9");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "cook.pantrypress.app");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");
        using var response = await gateway.CreateBrowser().SendAsync(request);

        var forwarded = Assert.Single(gateway.Forwarded);
        Assert.Equal(GatewayFactory.AdminOrigin.Host, forwarded.Header("X-Forwarded-Host"));
        Assert.Equal(Uri.UriSchemeHttps, forwarded.Header("X-Forwarded-Proto"));
        Assert.DoesNotContain(",", forwarded.Header("X-Forwarded-For") ?? string.Empty, StringComparison.Ordinal);
        Assert.Null(forwarded.Header("Forwarded"));
    }

    [Fact]
    public async Task The_admin_dev_server_is_told_no_more_than_the_api_is()
    {
        // The Development catch-all forwards the SPA itself. The strip runs ahead of routing, so it covers that route
        // too rather than only the admin surface.
        await using var gateway = new GatewayFactory("Development");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/posts/new");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "cook.pantrypress.app");
        using var response = await gateway.CreateBrowser().SendAsync(request);

        var forwarded = Assert.Single(gateway.Forwarded);
        Assert.Equal(GatewayFactory.AdminDestination, forwarded.Uri.Host);
        Assert.Equal(GatewayFactory.AdminOrigin.Host, forwarded.Header("X-Forwarded-Host"));
    }
}
