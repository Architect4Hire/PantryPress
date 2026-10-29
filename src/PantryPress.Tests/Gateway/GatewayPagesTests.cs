using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PantryPress.Gateway.Hosting;
using PantryPress.ServiceDefaults.EdgeHardening;

namespace PantryPress.Tests.Gateway;

// PPL.3: the gateway serves the platform home's pages from the admin app's build (PP-D19, ADR 0019). A public path gets
// its prerendered document, every other page path the browser-rendered one, and a complete session at '/' or
// '/pricing' is sent to the dashboard from the gateway's own cookie before any document is read.
public sealed partial class GatewayPagesTests
{
    private const string Session = "/api/v1/account/session";

    private const string Hsts = "max-age=31536000; includeSubDomains";

    private static readonly string[] Redirected = ["/", "/pricing"];

    public static TheoryData<string> PublicPaths => [.. AdminAppBuild.PublicPaths];

    // ---- Anonymous and half-finished: the documents themselves ----

    [Theory]
    [MemberData(nameof(PublicPaths))]
    public async Task An_anonymous_visitor_gets_the_prerendered_document(string path)
    {
        await using var gateway = new GatewayFactory("Production");

        using var response = await gateway.CreateBrowser().GetAsync(path);

        await AssertDocumentAsync(response, AdminAppBuild.Marker(path));
        Assert.Empty(gateway.Forwarded);
        Assert.Empty(gateway.SessionCalls);
    }

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/dashboard/x")]
    [InlineData("/sign-in")]
    [InlineData("/nope")]
    [InlineData("/Pricing")]
    [InlineData("/dashboard/sites/my-blog/domains/www.example.com")]
    [InlineData("/dashboard/account/export.csv")]
    [InlineData("/index.html")]
    [InlineData("/pricing/index.html")]
    [InlineData("/index.csr.html")]
    public async Task Every_other_page_path_gets_the_browser_rendered_document(string path)
    {
        // The app's router draws the page, the not-found page included, so an unknown page path is a 200 here. The
        // build's own HTML files are reached only by their page path: asking for /index.html by name cannot step
        // around the signed-in redirect. Under the dashboard a dotted last segment is still a page.
        await using var gateway = new GatewayFactory("Production");

        using var response = await gateway.CreateBrowser().GetAsync(path);

        await AssertDocumentAsync(response, AdminAppBuild.BrowserMarker);
    }

    [Fact]
    public async Task A_head_request_is_answered_like_a_get_without_a_body()
    {
        await using var gateway = new GatewayFactory("Production");

        using var response = await gateway.CreateBrowser().SendAsync(new HttpRequestMessage(HttpMethod.Head, "/"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AdminApp.DocumentCacheControl, response.Headers.CacheControl?.ToString());
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task A_document_the_browser_already_holds_is_revalidated_not_sent_again()
    {
        await using var gateway = new GatewayFactory("Production");
        var browser = gateway.CreateBrowser();
        using var first = await browser.GetAsync("/");

        using var again = new HttpRequestMessage(HttpMethod.Get, "/");
        again.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        using var response = await browser.SendAsync(again);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.Equal(AdminApp.DocumentCacheControl, response.Headers.CacheControl?.ToString());
        AssertSecurityHeaders(response, ContentSecurityPolicies.Document);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/pricing")]
    public async Task A_half_finished_sign_in_is_not_a_session(string path)
    {
        await using var gateway = new GatewayFactory(
            "Production",
            sessions: _ => SessionFixtures.Accepted(secondStepRequired: true));
        var browser = gateway.CreateBrowser();
        using var signIn = await browser.PostAsJsonAsync(Session, Credentials());
        Assert.NotNull(signIn.CookieValue(SessionCookie.SecondFactorName));

        using var response = await browser.GetAsync(path);

        await AssertDocumentAsync(response, AdminAppBuild.Marker(path));
    }

    // ---- A complete session ----

    [Theory]
    [InlineData("GET", "/")]
    [InlineData("GET", "/pricing")]
    [InlineData("HEAD", "/")]
    [InlineData("HEAD", "/pricing")]
    public async Task A_complete_session_is_sent_to_the_dashboard_without_asking_the_api(string method, string path)
    {
        await using var gateway = new GatewayFactory("Production", sessions: _ => SessionFixtures.Accepted());
        using var browser = await gateway.SignedInAsync();
        var calls = gateway.SessionCalls.Count;

        using var response = await browser.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        AssertRedirected(response);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(calls, gateway.SessionCalls.Count);
        Assert.Empty(gateway.Forwarded);
    }

    [Theory]
    [InlineData("/terms", null)]
    [InlineData("/privacy", null)]
    [InlineData("/acceptable-use", null)]
    [InlineData("/dmca", null)]
    [InlineData("/dashboard", AdminAppBuild.BrowserMarker)]
    [InlineData("/dashboard/x", AdminAppBuild.BrowserMarker)]
    [InlineData("/nope", AdminAppBuild.BrowserMarker)]
    public async Task A_complete_session_still_reads_every_other_page(string path, string? marker)
    {
        // The legal pages are public but never redirected: a signed-in owner must be able to read them.
        await using var gateway = new GatewayFactory("Production", sessions: _ => SessionFixtures.Accepted());
        using var browser = await gateway.SignedInAsync();

        using var response = await browser.GetAsync(path);

        await AssertDocumentAsync(response, marker ?? AdminAppBuild.Marker(path));
    }

    [Fact]
    public async Task A_revoked_session_is_redirected_once_then_signed_out_and_never_loops()
    {
        // The gateway cannot know the API has ended the session, so '/' sends it on. The dashboard's guard (PP3.8)
        // then asks the current session, meets session-ended, and the cookie goes with that answer; '/' is the
        // landing page from then on. The dashboard is not a redirected path, so the redirect has nowhere to repeat.
        await using var gateway = new GatewayFactory(
            "Production",
            sessions: request => request.RequestUri!.AbsolutePath.EndsWith("/sessions/current", StringComparison.Ordinal)
                ? SessionFixtures.Refused(HttpStatusCode.BadRequest, GatewayApi.SessionEndedCode, GatewayApi.SessionEndedTitle)
                : SessionFixtures.Accepted());
        using var browser = await gateway.SignedInAsync();

        using var landing = await browser.GetAsync("/");
        AssertRedirected(landing);

        using var dashboard = await browser.GetAsync(landing.Headers.Location);
        await AssertDocumentAsync(dashboard, AdminAppBuild.BrowserMarker);

        using var guard = await browser.GetAsync(Session);
        Assert.Equal(HttpStatusCode.Unauthorized, guard.StatusCode);
        Assert.StartsWith($"{SessionCookie.CompleteName}=;", guard.SetCookie(SessionCookie.CompleteName), StringComparison.Ordinal);

        using var signIn = await browser.GetAsync("/sign-in");
        await AssertDocumentAsync(signIn, AdminAppBuild.BrowserMarker);
        using var back = await browser.GetAsync("/");
        await AssertDocumentAsync(back, AdminAppBuild.Marker("/"));
    }

    [Fact]
    public async Task A_session_left_idle_past_its_sliding_window_is_not_redirected()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero));
        await using var gateway = new GatewayFactory("Production", sessions: _ => SessionFixtures.Accepted(), time: time);
        using var browser = await gateway.SignedInAsync();

        time.Advance(SessionCookie.CompleteLifetime + TimeSpan.FromMinutes(1));
        using var response = await browser.GetAsync("/");

        await AssertDocumentAsync(response, AdminAppBuild.Marker("/"));
    }

    [Fact]
    public async Task A_session_kept_busy_past_its_absolute_lifetime_is_cleared_not_redirected()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero));
        await using var gateway = new GatewayFactory("Production", sessions: _ => SessionFixtures.Accepted(), time: time);
        using var browser = await gateway.SignedInAsync();

        // Read every three hours, well inside the eight-hour sliding window, so only the twelve-hour bound can end it.
        for (var hours = 3; hours < 12; hours += 3)
        {
            time.Advance(TimeSpan.FromHours(3));
            using var stillOn = await browser.GetAsync("/");
            AssertRedirected(stillOn);
        }

        time.Advance(TimeSpan.FromHours(3));
        using var response = await browser.GetAsync("/");

        await AssertDocumentAsync(response, AdminAppBuild.Marker("/"));
        Assert.StartsWith($"{SessionCookie.CompleteName}=;", response.SetCookie(SessionCookie.CompleteName), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_forged_session_cookie_is_not_a_session()
    {
        await using var gateway = new GatewayFactory("Production");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", $"{SessionCookie.CompleteName}=CfDJ8forged");

        using var response = await gateway.CreateBare().SendAsync(request);

        await AssertDocumentAsync(response, AdminAppBuild.Marker("/"));
    }

    // ---- /api, hosts, and methods: unchanged ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unknown_api_path_is_still_the_gateways_problem_404(bool signedIn)
    {
        await using var gateway = new GatewayFactory("Production", sessions: _ => SessionFixtures.Accepted());
        using var browser = signedIn ? await gateway.SignedInAsync() : gateway.CreateBrowser();

        foreach (var path in new[] { "/api/nope", "/api", "/api/v2/x", "/api/public/v1/site" })
        {
            using var response = await browser.GetAsync(path);

            await AssertProblemAsync(response, HttpStatusCode.NotFound, GatewayApi.NotFoundCode);
            Assert.Equal(ContentSecurityPolicies.Api, Header(response, SecurityHeaders.ContentSecurityPolicy));
        }
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/terms")]
    [InlineData("/nope")]
    [InlineData("/" + AdminAppBuild.Script)]
    public async Task No_host_but_the_gateways_own_is_answered(string path)
    {
        await using var gateway = new GatewayFactory("Production");
        foreach (var host in new[] { "app.localhost", "www.localhost", "blog.localhost", "pantrypress.app.evil.test" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Host = host;

            using var response = await gateway.CreateBare().SendAsync(request);

            await AssertProblemAsync(response, HttpStatusCode.NotFound, GatewayApi.NotFoundCode);
        }
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task A_page_path_answers_reads_only(string method)
    {
        await using var gateway = new GatewayFactory("Production");

        using var response = await gateway.CreateBrowser().SendAsync(new HttpRequestMessage(new HttpMethod(method), "/nope"));

        await AssertProblemAsync(response, HttpStatusCode.MethodNotAllowed, GatewayApi.MethodNotAllowedCode);
        Assert.Contains("GET", response.Content.Headers.Allow);
        Assert.Empty(gateway.Forwarded);
    }

    // ---- The build's own files ----

    [Theory]
    [InlineData(AdminAppBuild.Script, "text/javascript")]
    [InlineData(AdminAppBuild.Chunk, "text/javascript")]
    [InlineData(AdminAppBuild.Styles, "text/css")]
    [InlineData(AdminAppBuild.Font, "font/woff2")]
    public async Task A_fingerprinted_file_is_cached_for_a_year(string file, string contentType)
    {
        await using var gateway = new GatewayFactory("Production");

        using var response = await gateway.CreateBrowser().GetAsync("/" + file);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(AdminApp.ImmutableCacheControl, response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", Header(response, SecurityHeaders.ContentTypeOptions));
    }

    [Fact]
    public async Task A_file_without_a_fingerprint_is_revalidated()
    {
        await using var gateway = new GatewayFactory("Production");

        using var response = await gateway.CreateBrowser().GetAsync("/" + AdminAppBuild.Favicon);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AdminApp.DocumentCacheControl, response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("/chunk-GONE1234.js")]
    [InlineData("/media/missing-AbCdEf12.woff2")]
    [InlineData("/.env")]
    [InlineData("/%2e%2e/appsettings.json")]
    [InlineData("/..%2fappsettings.json")]
    [InlineData("/media/..%2f..%2fappsettings.json")]
    [InlineData("/robots.txt")]
    public async Task A_file_the_build_does_not_serve_is_a_404_never_a_document(string path)
    {
        // A stale chunk asked for after a deployment must fail as a missing file, not run the app's HTML as a script.
        await using var gateway = new GatewayFactory("Production");

        using var response = await gateway.CreateBrowser().GetAsync(path);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, GatewayApi.NotFoundCode);
    }

    // ---- Development: the dev server serves the pages, the redirect still runs first ----

    [Theory]
    [InlineData("/")]
    [InlineData("/pricing")]
    public async Task In_development_a_complete_session_is_redirected_before_the_dev_server_is_asked(string path)
    {
        await using var gateway = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());
        using var browser = await gateway.SignedInAsync();

        using var response = await browser.GetAsync(path);

        AssertRedirected(response, ContentSecurityPolicies.DevelopmentDocument);
        Assert.Empty(gateway.Forwarded);
    }

    [Fact]
    public async Task In_development_the_dev_servers_documents_are_revalidated_and_its_files_left_alone()
    {
        var html = true;
        await using var gateway = new GatewayFactory("Development", forward: () =>
        {
            var answer = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html ? "<!doctype html>" : "export {};", System.Text.Encoding.UTF8, html ? "text/html" : "text/javascript"),
            };
            answer.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
            return answer;
        });

        using var document = await gateway.CreateBrowser().GetAsync("/");
        html = false;
        using var script = await gateway.CreateBrowser().GetAsync("/main.js");

        Assert.Equal(AdminApp.DocumentCacheControl, document.Headers.CacheControl?.ToString());
        Assert.Equal("max-age=3600", script.Headers.CacheControl?.ToString());
        Assert.Equal(2, gateway.Forwarded.Count);
    }

    // ---- What the gateway refuses to start with ----

    [Theory]
    [InlineData("index.html")]
    [InlineData("pricing/index.html")]
    [InlineData("terms/index.html")]
    [InlineData("index.csr.html")]
    public void A_build_missing_a_document_is_refused(string document)
    {
        var root = AdminAppBuild.Create(r => File.Delete(Path.Combine(r, document)));

        AssertRefused(Root(root), document);
    }

    [Fact]
    public void A_build_that_is_not_there_is_refused() =>
        AssertRefused(Root(Path.Combine(Path.GetTempPath(), "pantrypress-no-such-build")), "Gateway:AdminApp:Root");

    [Fact]
    public void A_deployed_gateway_without_a_build_is_refused() =>
        AssertRefused(Root(string.Empty), "Gateway:AdminApp:Root");

    [Theory]
    [InlineData("<script>document.querySelectorAll('link[data-beasties-media]')</script>")]
    [InlineData("<script type=\"module\">import('./x.js')</script>")]
    [InlineData("<script type=\"text/javascript\" id=\"ng-event-dispatch-contract\">(()=>{})()</script>")]
    [InlineData("<link rel=\"stylesheet\" href=\"s.css\" media=\"print\" onload=\"this.media='all'\">")]
    [InlineData("<svg/onload=x()>")]
    [InlineData("<a title=\"a>b\" onclick=x()>go</a>")]
    public void A_document_with_inline_script_is_refused(string script)
    {
        var root = AdminAppBuild.Create(r => AdminAppBuild.Write(
            r,
            "pricing/index.html",
            $"<!doctype html><html><head>{script}</head><body><h1>Pricing</h1></body></html>"));

        AssertRefused(Root(root), "pricing/index.html");
    }

    [Fact]
    public void A_build_is_not_served_in_development()
    {
        using var gateway = new GatewayFactory("Development", settings: Root(AdminAppBuild.Root));

        AssertRefused(gateway, "Development");
    }

    [Fact]
    public void The_dev_servers_route_is_not_allowed_beside_a_build()
    {
        using var gateway = new GatewayFactory("Production", settings: new Dictionary<string, string?>
        {
            [$"ReverseProxy:Routes:{GatewayRoutes.AdminDevServer}:ClusterId"] = "api",
            [$"ReverseProxy:Routes:{GatewayRoutes.AdminDevServer}:Match:Path"] = "{**catch-all}",
        });

        AssertRefused(gateway, GatewayRoutes.AdminDevServer);
    }

    public static TheoryData<string[], string[], string> InvalidPaths => new()
    {
        { ["/", "/pricing"], ["/", "/terms"], "/dashboard" },
        { ["/", "/pricing"], ["/", "/pricing"], "/pricing" },
        { ["/", "/pricing"], ["/", "/pricing"], "/" },
        { ["/", "/pricing"], [], "/dashboard" },
        { [], [], "/dashboard" },
        { ["/", "/pricing/"], ["/"], "/dashboard" },
        { ["/", "/Pricing"], ["/"], "/dashboard" },
        { ["/", "pricing"], ["/"], "/dashboard" },
        { ["/", "/api/x"], ["/"], "/dashboard" },
        { ["/", "/pricing", "/pricing"], ["/"], "/dashboard" },
        { ["/", "/pricing?x=1"], ["/"], "/dashboard" },
        { ["/", "/pricing"], ["/"], "dashboard" },
        { ["/", "/pricing"], ["/"], "/api/v1/x" },
    };

    [Theory]
    [MemberData(nameof(InvalidPaths))]
    public void Path_lists_that_could_loop_or_hide_a_page_are_refused(string[] publicPaths, string[] redirected, string home)
    {
        var options = new AdminAppOptions { SignedInHome = home };
        foreach (var path in publicPaths)
        {
            options.PublicPaths.Add(path);
        }

        foreach (var path in redirected)
        {
            options.SignedInRedirectPaths.Add(path);
        }

        Assert.Throws<InvalidOperationException>(() => AdminApp.Validate(options, development: true, devServerRoute: true));
    }

    [Fact]
    public async Task The_configured_redirect_never_lands_on_a_redirected_path()
    {
        await using var gateway = new GatewayFactory("Production");
        var options = (AdminAppOptions)gateway.Services.GetService(typeof(AdminAppOptions))!;

        Assert.Equal(Redirected, options.SignedInRedirectPaths);
        Assert.Equal(AdminAppBuild.PublicPaths, options.PublicPaths);
        Assert.DoesNotContain(options.SignedInHome, options.PublicPaths);
    }

    // ---- The pieces on their own ----

    [Theory]
    [InlineData("<script id=\"ng-state\" type=\"application/json\">{}</script>")]
    [InlineData("<script type=\"application/ld+json\">{}</script>")]
    [InlineData("<script src=\"main-YXEA34CX.js\" type=\"module\"></script>")]
    [InlineData("<p>Turn the oven on = 180°C</p>")]
    [InlineData("<p title=\"Sell online = easy\">Hi</p>")]
    [InlineData("<a href=\"/x\" data-on=\"onclick=\">x</a>")]
    public void Data_blocks_and_scripts_by_file_are_not_inline_script(string html) =>
        Assert.Null(AdminApp.InlineScriptIn(html));

    [Theory]
    [InlineData("main-YXEA34CX.js", true)]
    [InlineData("chunk-C1xRoO3R.js", true)]
    [InlineData("styles-IEXM7OZ5.css", true)]
    [InlineData("chunk-x_XEu4qQ.js", true)]
    [InlineData("media/fredoka-ABCD2345.woff2", true)]
    [InlineData("favicon.ico", false)]
    [InlineData("robots.txt", false)]
    [InlineData("main.js", false)]
    [InlineData("polyfills-ABC.js", false)]
    [InlineData("fredoka-semibold.woff2", false)]
    [InlineData("dm-sans-variable.woff2", false)]
    [InlineData("robot-chef-portrait.png", false)]
    [InlineData("logo-darkmode.svg", false)]
    [InlineData("og-card-1200x630.png", false)]
    [InlineData("fonts/fredoka-ABCD2345.woff2", false)]
    [InlineData("media/robot-chef-portrait.png", false)]
    [InlineData("media/fredoka-abcd2345.woff2", false)]
    [InlineData("chunk-x_XEu4qQ.css.map", false)]
    public void Only_a_fingerprinted_file_counts_as_one(string name, bool fingerprinted) =>
        Assert.Equal(fingerprinted, AdminApp.IsFingerprinted(name));

    private static Dictionary<string, string?> Credentials() => new()
    {
        ["email"] = SessionFixtures.Email,
        ["password"] = SessionFixtures.Passphrase,
    };

    private static Dictionary<string, string?> Root(string root) => new() { ["Gateway:AdminApp:Root"] = root };

    private static void AssertRefused(Dictionary<string, string?> settings, string expected)
    {
        using var gateway = new GatewayFactory("Production", settings: settings);
        AssertRefused(gateway, expected);
    }

    private static void AssertRefused(GatewayFactory gateway, string expected)
    {
        var failure = Assert.ThrowsAny<Exception>(() => gateway.CreateBrowser());
        Assert.Contains(expected, Unwrap(failure), StringComparison.Ordinal);
    }

    private static string Unwrap(Exception failure)
    {
        var messages = new List<string>();
        for (var current = failure; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    private static void AssertRedirected(
        HttpResponseMessage response,
        string policy = ContentSecurityPolicies.Document)
    {
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/dashboard", response.Headers.Location?.OriginalString);
        // A renewed session cookie adds the cookie handler's own no-cache beside it, which changes nothing.
        Assert.True(response.Headers.CacheControl?.NoStore, response.Headers.CacheControl?.ToString());
        AssertSecurityHeaders(response, policy);
        if (policy == ContentSecurityPolicies.Document)
        {
            Assert.Equal(Hsts, Header(response, SecurityHeaders.StrictTransportSecurity));
        }
    }

    private static async Task AssertDocumentAsync(HttpResponseMessage response, string marker)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        AssertRevalidated(response);
        Assert.Contains($"<h1>{marker}</h1>", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        AssertSecurityHeaders(response, ContentSecurityPolicies.Document);
        Assert.Equal(Hsts, Header(response, SecurityHeaders.StrictTransportSecurity));
    }

    // Never shared-cached, never kept without asking: no-cache, and nothing that would let a cache keep it longer.
    private static void AssertRevalidated(HttpResponseMessage response)
    {
        var cache = response.Headers.CacheControl;
        Assert.True(cache?.NoCache, cache?.ToString());
        Assert.False(cache!.Public);
        Assert.Null(cache.MaxAge);
        Assert.Null(cache.SharedMaxAge);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response, string policy)
    {
        Assert.Equal("nosniff", Header(response, SecurityHeaders.ContentTypeOptions));
        Assert.Equal("DENY", Header(response, SecurityHeaders.FrameOptions));
        Assert.Equal(ReferrerPolicies.Admin, Header(response, SecurityHeaders.ReferrerPolicy));
        Assert.Equal(PermissionsPolicies.DenyAll, Header(response, SecurityHeaders.PermissionsPolicy));
        Assert.Equal(policy, Header(response, SecurityHeaders.ContentSecurityPolicy));
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
}
