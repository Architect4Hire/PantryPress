using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PantryPress.Gateway.Hosting;

namespace PantryPress.Tests.Gateway;

// PP1.7: CORS is closed, same-origin only (ACCT-002, gateway.md). The admin app is served from this gateway's own
// origin, so no cross-origin caller is allowed and a write that says it came from elsewhere is refused outright.
public sealed class GatewayCorsTests
{
    private const string AdminRoute = "/api/v1/account/two-factor";
    private const string Elsewhere = "https://evil.example";

    [Theory]
    [InlineData("/api/v1/account/session")]
    [InlineData(AdminRoute)]
    public async Task A_preflight_is_answered_here_with_no_cors_headers_and_never_forwarded(string path)
    {
        await using var gateway = new GatewayFactory("Development");

        using var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", Elsewhere);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        using var response = await gateway.CreateBare().SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
        Assert.Empty(gateway.Forwarded);
    }

    [Fact]
    public async Task A_cross_origin_read_is_answered_without_permission_to_read_it()
    {
        await using var gateway = new GatewayFactory("Development");

        using var request = new HttpRequestMessage(HttpMethod.Get, AdminRoute);
        request.Headers.Add("Origin", Elsewhere);
        using var response = await gateway.CreateBare().SendAsync(request);

        // The browser is what stops the script reading this; the gateway's part is to grant nothing.
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData(Elsewhere)]
    [InlineData("http://app.localhost")] // our host, another scheme
    [InlineData("https://app.localhost:8443")] // our host and scheme, another port
    [InlineData("https://app.localhost.evil.example")] // our host as a prefix of theirs
    [InlineData("null")] // a sandboxed frame
    public async Task A_write_from_another_origin_is_refused_and_never_forwarded(string origin)
    {
        await using var gateway = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());
        using var browser = await gateway.SignedInAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/account/password/change")
        {
            Content = JsonContent.Create(new { currentPassword = "x", newPassword = "y" }),
        };
        request.Headers.TryAddWithoutValidation("Origin", origin);
        using var response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(GatewayCors.CrossOriginCode, problem.GetProperty("code").GetString());
        Assert.Equal(GatewayCors.CrossOriginTitle, problem.GetProperty("title").GetString());
        Assert.Empty(gateway.Forwarded);
    }

    [Fact]
    public async Task A_sign_in_from_another_origin_is_refused_before_the_api_is_asked()
    {
        await using var gateway = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());

        using var request = new HttpRequestMessage(HttpMethod.Post, SessionEndpoints.Base)
        {
            Content = JsonContent.Create(new { email = SessionFixtures.Email, password = SessionFixtures.Passphrase }),
        };
        request.Headers.Add("Origin", Elsewhere);
        using var response = await gateway.CreateBrowser().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(gateway.SessionCalls);
        Assert.Null(response.SetCookie(SessionCookie.CompleteName));
    }

    [Fact]
    public async Task A_write_from_the_admin_apps_own_origin_goes_through()
    {
        await using var gateway = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());
        using var browser = await gateway.SignedInAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/account/password/change")
        {
            Content = JsonContent.Create(new { currentPassword = "x", newPassword = "y" }),
        };
        request.Headers.Add("Origin", GatewayFactory.AdminOrigin.GetLeftPart(UriPartial.Authority));
        using var response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(gateway.Forwarded);
    }

    [Fact]
    public async Task A_write_with_no_origin_at_all_is_left_to_the_token_to_judge()
    {
        // A browser sends Origin on exactly the cross-site writes this is about. Other callers (a script, a test) send
        // none, and the antiforgery token is what they still have to produce.
        await using var gateway = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());
        using var browser = await gateway.SignedInAsync();

        using var response = await browser.PostAsJsonAsync(
            "/api/v1/account/password/change",
            new { currentPassword = "x", newPassword = "y" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
