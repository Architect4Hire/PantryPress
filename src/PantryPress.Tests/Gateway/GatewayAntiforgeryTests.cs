using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Gateway.Hosting;
using Yarp.ReverseProxy.Configuration;

namespace PantryPress.Tests.Gateway;

// PP1.7: a state-changing request through the gateway carries a token the admin app could only have read from this
// origin (ACCT-002, gateway.md). The session rides in a cookie, so without this a form on another site could make a
// signed-in person's browser write - and a sign-in a stranger triggers is a forgery too, which is why the gateway's
// own session routes are included.
public sealed class GatewayAntiforgeryTests
{
    private const string ForwardedWrite = "/api/v1/account/password/change";

    public static TheoryData<string, string> Writes => new()
    {
        { "POST", ForwardedWrite },
        { "PUT", ForwardedWrite },
        { "PATCH", ForwardedWrite },
        { "DELETE", ForwardedWrite },
        { "POST", SessionEndpoints.Base },
        { "POST", SessionEndpoints.Base + "/second-factor" },
        { "DELETE", SessionEndpoints.Base },
    };

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task A_write_without_a_token_is_refused_and_reaches_nothing(string method, string path)
    {
        await using var gateway = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());

        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { email = SessionFixtures.Email, password = SessionFixtures.Passphrase }),
        };
        using var response = await gateway.CreateBare().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(GatewayAntiforgery.FailedCode, problem.GetProperty("code").GetString());
        Assert.Equal(GatewayAntiforgery.FailedTitle, problem.GetProperty("title").GetString());
        Assert.True(response.Headers.CacheControl?.NoStore);

        // Neither the API behind the BFF nor the gateway's own session call was reached.
        Assert.Empty(gateway.Forwarded);
        Assert.Empty(gateway.SessionCalls);
        Assert.Null(response.SetCookie(SessionCookie.CompleteName));
    }

    [Fact]
    public async Task A_signed_in_write_is_refused_just_the_same()
    {
        // The session is exactly what a forged write would be spending, so holding one is no substitute for a token.
        await using var gateway = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());
        using var browser = await gateway.SignedInAsync();
        var session = await SessionCookieAsync(gateway);

        using var client = gateway.CreateBare();
        using var request = new HttpRequestMessage(HttpMethod.Post, ForwardedWrite)
        {
            Content = JsonContent.Create(new { currentPassword = "x", newPassword = "y" }),
        };
        request.Headers.Add("Cookie", session);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(gateway.Forwarded);
    }

    [Fact]
    public async Task The_cookie_alone_is_not_a_token()
    {
        await using var gateway = new GatewayFactory("Development");
        var (cookies, _) = await TokensAsync(gateway);

        using var request = new HttpRequestMessage(HttpMethod.Post, ForwardedWrite);
        request.Headers.Add("Cookie", cookies);
        using var response = await gateway.CreateBare().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(gateway.Forwarded);
    }

    [Fact]
    public async Task The_header_alone_is_not_a_token()
    {
        await using var gateway = new GatewayFactory("Development");
        var (_, token) = await TokensAsync(gateway);

        using var request = new HttpRequestMessage(HttpMethod.Post, ForwardedWrite);
        request.Headers.Add(GatewayAntiforgery.HeaderName, token);
        using var response = await gateway.CreateBare().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(gateway.Forwarded);
    }

    [Fact]
    public async Task A_token_minted_by_another_gateway_is_not_a_token_here()
    {
        // Which is what a token a reader could invent amounts to: it is protected by a key ring they do not have.
        await using var mine = new GatewayFactory("Development");
        await using var theirs = new GatewayFactory("Development");
        var (cookies, token) = await TokensAsync(theirs);

        using var request = new HttpRequestMessage(HttpMethod.Post, ForwardedWrite);
        request.Headers.Add("Cookie", cookies);
        request.Headers.Add(GatewayAntiforgery.HeaderName, token);
        using var response = await mine.CreateBare().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(mine.Forwarded);
    }

    [Fact]
    public async Task A_mismatched_pair_is_not_a_token()
    {
        await using var gateway = new GatewayFactory("Development");
        var (cookies, _) = await TokensAsync(gateway);
        var (_, otherToken) = await TokensAsync(gateway);

        using var request = new HttpRequestMessage(HttpMethod.Post, ForwardedWrite);
        request.Headers.Add("Cookie", cookies);
        request.Headers.Add(GatewayAntiforgery.HeaderName, otherToken);
        using var response = await gateway.CreateBare().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(gateway.Forwarded);
    }

    [Fact]
    public async Task A_matching_pair_goes_through()
    {
        await using var gateway = new GatewayFactory("Development");
        var (cookies, token) = await TokensAsync(gateway);

        using var request = new HttpRequestMessage(HttpMethod.Post, ForwardedWrite)
        {
            Content = JsonContent.Create(new { currentPassword = "x", newPassword = "y" }),
        };
        request.Headers.Add("Cookie", cookies);
        request.Headers.Add(GatewayAntiforgery.HeaderName, token);
        using var response = await gateway.CreateBare().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(gateway.Forwarded);
    }

    [Fact]
    public async Task The_token_is_the_gateways_own_business_and_is_not_forwarded()
    {
        await using var gateway = new GatewayFactory("Development");
        var (cookies, token) = await TokensAsync(gateway);

        using var request = new HttpRequestMessage(HttpMethod.Post, ForwardedWrite);
        request.Headers.Add("Cookie", cookies);
        request.Headers.Add(GatewayAntiforgery.HeaderName, token);
        using var response = await gateway.CreateBare().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(Assert.Single(gateway.Forwarded).Header(GatewayAntiforgery.HeaderName));
    }

    [Fact]
    public async Task A_read_hands_the_admin_app_a_token_it_can_use()
    {
        await using var gateway = new GatewayFactory("Development");

        // The app's first call at startup is this read, and a 401 is a perfectly good answer to it: what matters is
        // that it comes back with a token, so the sign-in that follows can be made.
        using var bootstrap = await gateway.CreateBare().GetAsync(SessionEndpoints.Base);

        Assert.Equal(HttpStatusCode.Unauthorized, bootstrap.StatusCode);
        var cookieToken = bootstrap.SetCookie(GatewayAntiforgery.CookieName);
        var requestToken = bootstrap.SetCookie(GatewayAntiforgery.TokenCookieName);
        Assert.NotNull(cookieToken);
        Assert.NotNull(requestToken);

        // The cookie token is the browser's to send and nobody's to read.
        Assert.Contains("; httponly", cookieToken, StringComparison.OrdinalIgnoreCase);

        // The request token exists to be read by the admin app's own script, and by nothing else: the __Host- prefix
        // is what keeps a sibling subdomain from writing either of them (PSEC-003).
        Assert.DoesNotContain("httponly", requestToken, StringComparison.OrdinalIgnoreCase);
        foreach (var cookie in new[] { cookieToken, requestToken })
        {
            Assert.StartsWith("__Host-", cookie, StringComparison.Ordinal);
            Assert.Contains("; path=/", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("; samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_read_that_already_has_a_token_is_not_handed_another()
    {
        await using var gateway = new GatewayFactory("Development");
        var (cookies, _) = await TokensAsync(gateway);

        using var request = new HttpRequestMessage(HttpMethod.Get, SessionEndpoints.Base);
        request.Headers.Add("Cookie", cookies);
        using var response = await gateway.CreateBare().SendAsync(request);

        Assert.Null(response.SetCookie(GatewayAntiforgery.TokenCookieName));
    }

    [Fact]
    public async Task A_refusal_hands_back_a_fresh_pair_so_the_next_attempt_works()
    {
        // A token goes stale for honest reasons: a cleared cookie, a gateway restart with a new key ring. The person
        // should not have to reload the admin app to recover.
        await using var gateway = new GatewayFactory("Development");

        using var refused = await gateway.CreateBare().PostAsJsonAsync(ForwardedWrite, new { currentPassword = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var cookies = Cookies(refused);
        var token = refused.CookieValue(GatewayAntiforgery.TokenCookieName);
        Assert.NotNull(token);

        using var retry = new HttpRequestMessage(HttpMethod.Post, ForwardedWrite);
        retry.Headers.Add("Cookie", cookies);
        retry.Headers.Add(GatewayAntiforgery.HeaderName, token);
        using var response = await gateway.CreateBare().SendAsync(retry);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_token_outlives_signing_in_and_signing_out()
    {
        // The token is bound to this origin, not to an account (PP1.7): there is no window in which a valid token is
        // refused because the session underneath it moved on.
        await using var gateway = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());
        var (cookies, token) = await TokensAsync(gateway);

        using var client = gateway.CreateBare();
        using var signInRequest = new HttpRequestMessage(HttpMethod.Post, SessionEndpoints.Base)
        {
            Content = JsonContent.Create(new { email = SessionFixtures.Email, password = SessionFixtures.Passphrase }),
        };
        signInRequest.Headers.Add("Cookie", cookies);
        signInRequest.Headers.Add(GatewayAntiforgery.HeaderName, token);
        using var signedIn = await client.SendAsync(signInRequest);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);

        var session = Assert.IsType<string>(signedIn.CookieValue(SessionCookie.CompleteName));
        using var signOutRequest = new HttpRequestMessage(HttpMethod.Delete, SessionEndpoints.Base);
        signOutRequest.Headers.Add("Cookie", $"{cookies}; {SessionCookie.CompleteName}={session}");
        signOutRequest.Headers.Add(GatewayAntiforgery.HeaderName, token);
        using var signedOut = await client.SendAsync(signOutRequest);

        // The same token the app read before signing in still works after signing in, and after signing out.
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task A_read_needs_no_token(string method)
    {
        await using var gateway = new GatewayFactory("Development");

        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1/account/two-factor");
        using var response = await gateway.CreateBare().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(gateway.Forwarded);
    }

    [Fact]
    public async Task Over_plain_http_there_is_no_token_to_hand_out_and_no_write_to_accept()
    {
        // The cookie that carries the token is Secure, so over plain HTTP a browser would never send it back. A read
        // still works; a write is refused rather than failing in some other way. Nobody reaches the admin app like
        // this - the edge is HTTPS - and PP1.8 adds the redirect and HSTS that say so.
        await using var gateway = new GatewayFactory("Development");
        var client = gateway.CreateBare();

        using var read = await client.GetAsync("http://app.localhost/api/v1/account/two-factor");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Null(read.SetCookie(GatewayAntiforgery.TokenCookieName));

        using var write = await client.PostAsJsonAsync("http://app.localhost/api/v1/account/password/change", new { x = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, write.StatusCode);
        var problem = await write.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(GatewayAntiforgery.FailedCode, problem.GetProperty("code").GetString());
        Assert.Single(gateway.Forwarded);
    }

    [Fact]
    public async Task Paths_outside_the_admin_surface_are_left_as_pp15_left_them()
    {
        // Nothing under /api but the admin surface is reachable here, so there is nothing to forge: a write to one is
        // the gateway's own 404, not a puzzle about tokens.
        await using var gateway = new GatewayFactory("Development");

        using var response = await gateway.CreateBare().PostAsJsonAsync("/api/publishing/v1/posts", new { title = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(GatewayApi.NotFoundCode, problem.GetProperty("code").GetString());
        Assert.Empty(gateway.Forwarded);
    }

    [Fact]
    public async Task The_admin_app_itself_is_not_an_api_surface()
    {
        // The Development catch-all serves the SPA from the dev server; antiforgery guards the API, which is where a
        // forged write would have to land.
        await using var gateway = new GatewayFactory("Development");

        using var response = await gateway.CreateBare().PostAsJsonAsync("/posts/new", new { draft = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GatewayFactory.AdminDestination, Assert.Single(gateway.Forwarded).Uri.Host);
    }

    [Fact]
    public async Task The_surface_antiforgery_guards_is_the_surface_that_is_forwarded()
    {
        // The middleware filters on a path; the route is configuration. If they ever part company, writes would go
        // through unguarded.
        await using var gateway = new GatewayFactory("Development");

        var routes = gateway.Services.GetRequiredService<IProxyConfigProvider>()
            .GetConfig()
            .Routes;

        var admin = Assert.Single(routes, route => route.RouteId == GatewayRoutes.AdminApi);
        Assert.StartsWith(GatewayRoutes.AdminApiPath, admin.Match.Path, StringComparison.Ordinal);
        Assert.StartsWith(GatewayRoutes.AdminApiPath, SessionEndpoints.Base, StringComparison.Ordinal);
    }

    /// <summary>A fresh pair, as a browser would hold them: the cookies to send back, and the token to echo.</summary>
    private static async Task<(string Cookies, string Token)> TokensAsync(GatewayFactory gateway)
    {
        using var bootstrap = await gateway.CreateBare().GetAsync(SessionEndpoints.Base);
        var token = Assert.IsType<string>(bootstrap.CookieValue(GatewayAntiforgery.TokenCookieName));
        return (Cookies(bootstrap), token);
    }

    private static string Cookies(HttpResponseMessage response)
    {
        var cookieToken = Assert.IsType<string>(response.CookieValue(GatewayAntiforgery.CookieName));
        var requestToken = Assert.IsType<string>(response.CookieValue(GatewayAntiforgery.TokenCookieName));
        return $"{GatewayAntiforgery.CookieName}={cookieToken}; {GatewayAntiforgery.TokenCookieName}={requestToken}";
    }

    private static async Task<string> SessionCookieAsync(GatewayFactory gateway)
    {
        using var browser = gateway.CreateBrowser();
        using var response = await browser.PostAsJsonAsync(
            SessionEndpoints.Base,
            new { email = SessionFixtures.Email, password = SessionFixtures.Passphrase });
        var ticket = Assert.IsType<string>(response.CookieValue(SessionCookie.CompleteName));
        return $"{SessionCookie.CompleteName}={ticket}";
    }
}
