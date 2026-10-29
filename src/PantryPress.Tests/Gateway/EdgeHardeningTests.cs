using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PantryPress.Gateway.Hosting;
using PantryPress.ServiceDefaults.EdgeHardening;

namespace PantryPress.Tests.Gateway;

// PP1.8: what every answer carries, how much a request may weigh, who may say where it came from, and the limit that
// was waiting on all three (PSEC-002, gateway.md).
public sealed class EdgeHardeningTests
{
    private const string AdminRoute = "/api/v1/account/two-factor";
    private const string SessionRoute = "/api/v1/account/session";
    private const string Proxy = "10.0.0.4";
    private const string Browser = "203.0.113.9";

    [Theory]
    [InlineData(AdminRoute)]          // forwarded to the API
    [InlineData(SessionRoute)]        // answered by the gateway
    [InlineData("/api/v2/account")]   // a version nobody declared: the problem 404
    [InlineData("/")]                 // the Development catch-all to the admin dev server
    public async Task Every_answer_carries_the_security_headers(string path)
    {
        await using var gateway = new GatewayFactory("Development");

        using var response = await gateway.CreateBrowser().GetAsync(path);

        Assert.Equal("nosniff", Header(response, SecurityHeaders.ContentTypeOptions));
        Assert.Equal("DENY", Header(response, SecurityHeaders.FrameOptions));
        Assert.Equal(ReferrerPolicies.Admin, Header(response, SecurityHeaders.ReferrerPolicy));
        Assert.Equal(PermissionsPolicies.DenyAll, Header(response, SecurityHeaders.PermissionsPolicy));
        Assert.NotNull(Header(response, SecurityHeaders.ContentSecurityPolicy));
    }

    [Fact]
    public async Task An_api_answer_is_denied_everything_and_a_document_gets_the_apps_own_policy()
    {
        // JSON is not a document; if a browser is ever talked into rendering one, it may load nothing at all.
        await using var gateway = new GatewayFactory("Development");
        using var browser = gateway.CreateBrowser();

        using var api = await browser.GetAsync(AdminRoute);
        using var document = await browser.GetAsync("/");

        Assert.Equal(ContentSecurityPolicies.Api, Header(api, SecurityHeaders.ContentSecurityPolicy));
        Assert.Equal(
            ContentSecurityPolicies.DevelopmentDocument,
            Header(document, SecurityHeaders.ContentSecurityPolicy));
    }

    [Fact]
    public async Task Outside_development_the_document_policy_is_the_strict_one()
    {
        await using var gateway = new GatewayFactory("Production");

        using var response = await gateway.CreateBrowser().GetAsync(AdminRoute);

        Assert.Equal(ContentSecurityPolicies.Api, Header(response, SecurityHeaders.ContentSecurityPolicy));
        Assert.DoesNotContain("unsafe-eval", ContentSecurityPolicies.Document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_carries_them_too()
    {
        // The answers that never reach an endpoint are exactly the ones a missing header would be worst on.
        await using var gateway = new GatewayFactory("Development");
        using var bare = gateway.CreateBare();

        using var request = new HttpRequestMessage(HttpMethod.Post, SessionRoute);
        request.Headers.TryAddWithoutValidation("Origin", "https://evil.example");
        using var crossOrigin = await bare.SendAsync(request);

        using var noToken = await bare.PostAsJsonAsync(SessionRoute, new { email = "a@b.test", password = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, crossOrigin.StatusCode);
        Assert.Equal(GatewayCors.CrossOriginCode, await crossOrigin.CodeAsync());
        Assert.Equal("nosniff", Header(crossOrigin, SecurityHeaders.ContentTypeOptions));
        Assert.Equal(ContentSecurityPolicies.Api, Header(crossOrigin, SecurityHeaders.ContentSecurityPolicy));

        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Equal(GatewayAntiforgery.FailedCode, await noToken.CodeAsync());
        Assert.Equal("nosniff", Header(noToken, SecurityHeaders.ContentTypeOptions));
        Assert.Equal("DENY", Header(noToken, SecurityHeaders.FrameOptions));
    }

    [Fact]
    public async Task Hsts_is_claimed_outside_development_and_never_locally()
    {
        await using var local = new GatewayFactory("Development");
        await using var deployed = new GatewayFactory("Production");

        using var development = await local.CreateBrowser().GetAsync(AdminRoute);
        using var production = await deployed.CreateBrowser().GetAsync(AdminRoute);

        Assert.Null(Header(development, SecurityHeaders.StrictTransportSecurity));
        Assert.Equal("max-age=31536000; includeSubDomains", Header(production, SecurityHeaders.StrictTransportSecurity));
        Assert.DoesNotContain(
            "preload",
            Header(production, SecurityHeaders.StrictTransportSecurity)!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hsts_is_never_claimed_over_plain_http()
    {
        // A browser would ignore it, and sending it would only invite a stripping proxy to think it had done something.
        await using var gateway = new GatewayFactory("Production");

        using var response = await gateway.CreatePlain().GetAsync(AdminRoute);

        Assert.Null(Header(response, SecurityHeaders.StrictTransportSecurity));
    }

    [Theory]
    [InlineData("GET", "/api/v1/account/two-factor?page=2")]
    [InlineData("POST", "/api/v1/account/session")]
    public async Task A_plain_http_request_is_sent_to_the_same_address_over_tls(string method, string path)
    {
        // 307, so the method and body survive the trip rather than a write quietly becoming a read.
        await using var gateway = new GatewayFactory("Development");

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await gateway.CreatePlain().SendAsync(request);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal($"https://localhost{path}", response.Headers.Location?.ToString());
        Assert.Empty(gateway.Forwarded);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task A_probe_is_never_redirected(string path)
    {
        // Container Apps probes a container over plain HTTP; a redirect would make a failing process of a healthy one.
        await using var gateway = new GatewayFactory("Development", probe: () => new HttpResponseMessage(HttpStatusCode.OK));

        using var response = await gateway.CreatePlain().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_known_proxys_word_about_the_browser_is_taken()
    {
        await using var gateway = Trusting(Proxy);

        using var request = new HttpRequestMessage(HttpMethod.Get, AdminRoute);
        request.Headers.TryAddWithoutValidation(GatewayFactory.RemoteAddressHeader, Proxy);
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", Browser);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        using var response = await gateway.CreateBrowser().SendAsync(request);

        // The gateway re-declares what it now knows, so the API is told about the browser rather than the hop.
        var forwarded = Assert.Single(gateway.Forwarded);
        Assert.Equal(Browser, forwarded.Header("X-Forwarded-For"));
        Assert.Equal(Uri.UriSchemeHttps, forwarded.Header("X-Forwarded-Proto"));
    }

    [Fact]
    public async Task A_stranger_saying_the_same_thing_is_not_believed()
    {
        await using var gateway = Trusting(Proxy);

        using var request = new HttpRequestMessage(HttpMethod.Get, AdminRoute);
        request.Headers.TryAddWithoutValidation(GatewayFactory.RemoteAddressHeader, "198.51.100.7");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", Browser);
        using var response = await gateway.CreateBrowser().SendAsync(request);

        // The API is told who actually connected, not who the caller said it was speaking for.
        var forwarded = Assert.Single(gateway.Forwarded);
        Assert.Equal("198.51.100.7", forwarded.Header("X-Forwarded-For"));
    }

    [Fact]
    public async Task With_no_proxy_configured_nobody_is_believed()
    {
        // The local and test default, and the production default until infrastructure writes a peer down.
        await using var gateway = new GatewayFactory("Development");

        using var request = new HttpRequestMessage(HttpMethod.Get, AdminRoute);
        request.Headers.TryAddWithoutValidation(GatewayFactory.RemoteAddressHeader, Proxy);
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", Browser);
        using var response = await gateway.CreateBrowser().SendAsync(request);

        var forwarded = Assert.Single(gateway.Forwarded);
        Assert.Equal(Proxy, forwarded.Header("X-Forwarded-For"));
    }

    [Fact]
    public async Task Only_as_many_hops_as_were_configured_are_believed()
    {
        // One trusted hop: the nearest entry is the one it added, and anything further back is the client's own list.
        await using var gateway = Trusting(Proxy);

        using var request = new HttpRequestMessage(HttpMethod.Get, AdminRoute);
        request.Headers.TryAddWithoutValidation(GatewayFactory.RemoteAddressHeader, Proxy);
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", $"192.0.2.1, {Browser}");
        using var response = await gateway.CreateBrowser().SendAsync(request);

        var forwarded = Assert.Single(gateway.Forwarded);
        Assert.Equal(Browser, forwarded.Header("X-Forwarded-For"));
    }

    [Fact]
    public async Task A_trusted_proxy_is_what_lets_a_write_happen_at_the_edge()
    {
        // The production shape: TLS ends at Front Door and the gateway is reached over plain HTTP. Without this step
        // the admin surface is read-only - no token is minted and no write is accepted (PP1.7). With it, the scheme
        // is the browser's again and the bootstrap read hands out the pair a write needs.
        await using var trusting = Trusting(Proxy);
        await using var plain = new GatewayFactory("Development");

        using var withProxy = new HttpRequestMessage(HttpMethod.Get, SessionRoute);
        withProxy.Headers.TryAddWithoutValidation(GatewayFactory.RemoteAddressHeader, Proxy);
        withProxy.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        using var believed = await trusting.CreatePlain().SendAsync(withProxy);

        using var alone = new HttpRequestMessage(HttpMethod.Get, SessionRoute);
        alone.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        using var ignored = await plain.CreatePlain().SendAsync(alone);

        Assert.NotNull(believed.CookieValue(GatewayAntiforgery.TokenCookieName));
        Assert.Equal(HttpStatusCode.TemporaryRedirect, ignored.StatusCode);
        Assert.Null(ignored.CookieValue(GatewayAntiforgery.TokenCookieName));
    }

    [Theory]
    [InlineData("X-Azure-FDID")]
    [InlineData("X-Azure-ClientIP")]
    [InlineData("X-Azure-SocketIP")]
    [InlineData("X-Original-Host")]
    [InlineData("X-Original-URL")]
    [InlineData("X-Real-IP")]
    [InlineData("X-ProxyUser-Ip")]
    public async Task The_headers_a_rewriting_proxy_sets_are_a_clients_to_invent_and_never_survive(string header)
    {
        // X-Azure-FDID especially: PP9.3 proves a request came through Front Door with it, and a client that could
        // forge one would walk straight past that check.
        await using var gateway = new GatewayFactory("Development");

        using var request = new HttpRequestMessage(HttpMethod.Get, AdminRoute);
        request.Headers.TryAddWithoutValidation(header, "invented");
        using var response = await gateway.CreateBrowser().SendAsync(request);

        var forwarded = Assert.Single(gateway.Forwarded);
        Assert.DoesNotContain("invented", forwarded.Header(header) ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("cook.localhost")]
    [InlineData("app.localhost")]
    [InlineData("www.localhost")]
    [InlineData("app.pantrypress.app")]
    [InlineData("www.pantrypress.app")]
    [InlineData("evil.example")]
    [InlineData("localhost.evil.example")]
    [InlineData("pantrypress.app.evil.example")]
    public async Task A_host_that_is_not_ours_gets_the_neutral_404(string host)
    {
        // Front Door routes a tenant host to the public site; one arriving here came from somewhere that is not a
        // browser following our DNS, and it is told nothing about what it guessed. The platform home is the apex
        // (PP-D19): app. and www. are Front Door's 301s to it, never hosts the gateway answers.
        await using var gateway = new GatewayFactory("Development");

        using var request = new HttpRequestMessage(HttpMethod.Get, AdminRoute);
        request.Headers.Host = host;
        using var response = await gateway.CreateBare().SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(GatewayApi.NotFoundCode, await response.CodeAsync());
        Assert.Empty(gateway.Forwarded);
    }

    [Fact]
    public async Task Our_own_host_with_a_trailing_dot_is_still_ours()
    {
        // The same name to DNS, and a difference a check that compared strings would miss.
        await using var gateway = new GatewayFactory("Development");

        using var request = new HttpRequestMessage(HttpMethod.Get, AdminRoute);
        request.Headers.Host = "localhost.";
        using var response = await gateway.CreateBare().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(gateway.Forwarded);
    }

    [Fact]
    public async Task A_probe_answers_on_any_host()
    {
        await using var gateway = new GatewayFactory("Development", probe: () => new HttpResponseMessage(HttpStatusCode.OK));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Host = "10.0.0.17";
        using var response = await gateway.CreateBare().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void A_deployed_host_will_not_start_trusting_nobody_by_accident()
    {
        // Everything the empty default costs is invisible locally, because Kestrel terminates TLS itself in a local
        // run: per-address limits collapse into one bucket tighter than the ceiling they replaced, no HSTS goes
        // out, and every admin write is refused. So a deployed host says who its proxy is, or says in writing that
        // it has none.
        using var gateway = new GatewayFactory(
            "Production",
            settings: new Dictionary<string, string?> { ["Edge:TrustedProxies:TrustNobody"] = "false" });

        var failure = Assert.ThrowsAny<Exception>(() => gateway.CreateBrowser());

        Assert.Contains("Edge:TrustedProxies", Unwrap(failure), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deployed_host_that_trusts_nobody_does_not_redirect_itself_into_a_loop()
    {
        // The pair that has to agree: with an HTTPS port configured but no proxy to say what scheme the browser
        // used, every request looks like plain HTTP, so a redirect would send the edge back to an address it just
        // came from, for ever. The redirect is therefore not registered at all.
        await using var gateway = new GatewayFactory("Production");

        using var response = await gateway.CreatePlain().GetAsync(AdminRoute);

        Assert.NotEqual(HttpStatusCode.TemporaryRedirect, response.StatusCode);
    }

    [Fact]
    public async Task A_trusted_proxy_may_only_name_a_host_the_gateway_answers_on()
    {
        // Believing a peer about the host is bounded by the same list the host check uses, so a peer on the
        // network cannot introduce a host of its own invention.
        await using var gateway = Trusting(Proxy);

        using var request = new HttpRequestMessage(HttpMethod.Get, AdminRoute);
        request.Headers.TryAddWithoutValidation(GatewayFactory.RemoteAddressHeader, Proxy);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "invented.example");
        using var response = await gateway.CreateBrowser().SendAsync(request);

        // The invented host was not taken, so the request is still on the gateway's own and is answered normally.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(gateway.Forwarded);
    }

    [Fact]
    public async Task Our_own_host_in_any_case_is_still_ours()
    {
        await using var gateway = new GatewayFactory("Development");

        using var request = new HttpRequestMessage(HttpMethod.Get, AdminRoute);
        request.Headers.Host = "LOCALHOST";
        using var response = await gateway.CreateBare().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_header_the_api_set_itself_is_what_the_browser_gets()
    {
        // YARP copies the destination's response headers, and the gateway's own writer does not overwrite a header
        // that is already there - which is the seam PP9's generated per-site policy needs. The two agree today
        // because both hosts set the same constants; this pins which one wins if they ever stop agreeing.
        await using var gateway = new GatewayFactory("Development", forward: () =>
        {
            var answer = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("forwarded") };
            answer.Headers.TryAddWithoutValidation(SecurityHeaders.ReferrerPolicy, "unsafe-url");
            return answer;
        });

        using var response = await gateway.CreateBrowser().GetAsync(AdminRoute);

        Assert.Equal("unsafe-url", Header(response, SecurityHeaders.ReferrerPolicy));
    }

    [Fact]
    public async Task A_body_over_the_limit_is_refused_before_anything_reads_it()
    {
        await using var gateway = new GatewayFactory("Development");

        using var request = new HttpRequestMessage(HttpMethod.Post, AdminRoute)
        {
            Content = Body(RequestLimits.AdminDefault + 1),
        };
        using var response = await gateway.CreateBare().SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(GatewayApi.TooLargeCode, await response.CodeAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(GatewayApi.TooLargeTitle, problem.RootElement.GetProperty("title").GetString());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));

        // Nothing was streamed to the API, which would have turned the refusal into a 502.
        Assert.Empty(gateway.Forwarded);
    }

    [Fact]
    public async Task A_refusal_has_a_body_even_for_a_caller_that_did_not_ask_for_json()
    {
        // A browser form post sends Accept: text/html, which is exactly the shape of an over-sized upload - and the
        // problem-details service declines to write for it, which would leave a bodiless 413.
        await using var gateway = new GatewayFactory("Development");

        using var request = new HttpRequestMessage(HttpMethod.Post, AdminRoute)
        {
            Content = Body(RequestLimits.AdminDefault + 1),
        };
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
        using var response = await gateway.CreateBare().SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(GatewayApi.TooLargeCode, await response.CodeAsync());
    }

    [Fact]
    public async Task A_body_at_the_limit_goes_through()
    {
        await using var gateway = new GatewayFactory("Development");

        using var request = new HttpRequestMessage(HttpMethod.Post, AdminRoute) { Content = Body(RequestLimits.AdminDefault) };
        using var response = await gateway.CreateBrowser().SendAsync(request);

        Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Single(gateway.Forwarded);
    }

    [Fact]
    public async Task Signing_in_is_limited_per_address()
    {
        // The limit that was waiting on the trusted-proxy step: one caller can no longer spend everyone's allowance.
        await using var gateway = new GatewayFactory(
            "Development",
            sessions: _ => SessionFixtures.Accepted(),
            settings: new Dictionary<string, string?> { ["Gateway:SessionPerIpPerMinute"] = "2" });

        var answers = new List<HttpResponseMessage>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            answers.Add(await SignInAsync(gateway, Browser));
        }

        Assert.Equal(HttpStatusCode.OK, answers[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, answers[1].StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, answers[2].StatusCode);
        Assert.Equal(GatewayApi.TooManyRequestsCode, await answers[2].CodeAsync());
        Assert.NotNull(answers[2].Headers.RetryAfter);

        // The refused attempt never asked ApiService to check anything.
        Assert.Equal(2, gateway.SessionCalls.Count);
        answers.ForEach(answer => answer.Dispose());
    }

    [Fact]
    public async Task Another_address_has_its_own_allowance()
    {
        await using var gateway = new GatewayFactory(
            "Development",
            sessions: _ => SessionFixtures.Accepted(),
            settings: new Dictionary<string, string?> { ["Gateway:SessionPerIpPerMinute"] = "1" });

        using var spent = await SignInAsync(gateway, Browser);
        using var refused = await SignInAsync(gateway, Browser);
        using var neighbour = await SignInAsync(gateway, "198.51.100.7");

        Assert.Equal(HttpStatusCode.OK, spent.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, neighbour.StatusCode);
    }

    /// <summary>A start-up failure arrives wrapped; the message that explains it is somewhere inside.</summary>
    private static string Unwrap(Exception failure)
    {
        var messages = new List<string>();
        for (var current = failure; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    private static GatewayFactory Trusting(string proxy) =>
        new(
            "Development",
            settings: new Dictionary<string, string?>
            {
                ["Edge:TrustedProxies:KnownProxies:0"] = proxy,
                ["Edge:TrustedProxies:ForwardLimit"] = "1",
            });

    private static async Task<HttpResponseMessage> SignInAsync(GatewayFactory gateway, string address)
    {
        using var browser = gateway.CreateBrowser();
        browser.DefaultRequestHeaders.Add(GatewayFactory.RemoteAddressHeader, address);
        return await browser.PostAsJsonAsync(
            SessionRoute,
            new { email = SessionFixtures.Email, password = SessionFixtures.Passphrase });
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
