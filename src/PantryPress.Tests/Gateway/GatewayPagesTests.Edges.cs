using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Time.Testing;
using PantryPress.Gateway.Hosting;
using PantryPress.ServiceDefaults.EdgeHardening;

namespace PantryPress.Tests.Gateway;

// PPL.3, the edges: the prompt's request table cell by cell, cookies that only look like a session, the ways a public
// path can be spelled, revalidation, what a cacheable answer may carry, and the paths that are never pages.
public sealed partial class GatewayPagesTests
{
    public enum Visitor
    {
        Anonymous,
        HalfFinished,
        Complete,
        Revoked,
    }

    // The prompt's table: each visitor against each path, with the status, Location, and Cache-Control it gets. A revoked
    // session still holds a ticket the gateway believes, so it is answered as a complete one; the API's word on it comes
    // with the dashboard's own check (the loop walk above).
    public static TheoryData<Visitor, string, HttpStatusCode, string?, string> RequestTable()
    {
        var table = new TheoryData<Visitor, string, HttpStatusCode, string?, string>();
        foreach (var visitor in Enum.GetValues<Visitor>())
        {
            var session = visitor is Visitor.Complete or Visitor.Revoked;
            table.Add(visitor, "/", session ? HttpStatusCode.Found : HttpStatusCode.OK, session ? "/dashboard" : null, session ? "no-store" : "no-cache");
            table.Add(visitor, "/pricing", session ? HttpStatusCode.Found : HttpStatusCode.OK, session ? "/dashboard" : null, session ? "no-store" : "no-cache");
            table.Add(visitor, "/terms", HttpStatusCode.OK, null, "no-cache");
            table.Add(visitor, "/dashboard/x", HttpStatusCode.OK, null, "no-cache");
            table.Add(visitor, "/nope", HttpStatusCode.OK, null, "no-cache");
            table.Add(visitor, "/api/nope", HttpStatusCode.NotFound, null, "no-store");
        }

        return table;
    }

    [Theory]
    [MemberData(nameof(RequestTable))]
    public async Task The_request_table(Visitor visitor, string path, HttpStatusCode status, string? location, string cacheControl)
    {
        await using var gateway = new GatewayFactory("Production", sessions: SessionsFor(visitor));
        using var browser = await VisitorAsync(gateway, visitor);

        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head })
        {
            using var response = await browser.SendAsync(new HttpRequestMessage(method, path));

            Assert.Equal(status, response.StatusCode);
            Assert.Equal(location, response.Headers.Location?.OriginalString);
            var cache = response.Headers.CacheControl;
            Assert.True(cacheControl == "no-store" ? cache?.NoStore : cache?.NoCache, $"{method} {path}: {cache}");
            Assert.False(cache?.Public ?? false);
            Assert.Equal("nosniff", Header(response, SecurityHeaders.ContentTypeOptions));
            Assert.NotNull(Header(response, SecurityHeaders.ContentSecurityPolicy));
        }

        Assert.Empty(gateway.Forwarded);
    }

    // ---- Cookies that only look like a session ----

    [Fact]
    public async Task A_half_finished_ticket_under_the_session_cookie_name_is_not_a_session()
    {
        await using var gateway = new GatewayFactory(
            "Production",
            sessions: _ => SessionFixtures.Accepted(secondStepRequired: true));
        using var signIn = await gateway.CreateBrowser().PostAsJsonAsync(Session, Credentials());
        var pending = signIn.CookieValue(SessionCookie.SecondFactorName);
        Assert.NotNull(pending);

        await AssertNotASessionAsync(gateway, $"{SessionCookie.CompleteName}={pending}");
    }

    [Fact]
    public async Task A_session_ticket_that_was_tampered_with_or_renamed_is_not_a_session()
    {
        await using var gateway = new GatewayFactory("Production", sessions: _ => SessionFixtures.Accepted());
        var ticket = await CompleteTicketAsync(gateway);

        // Proof the ticket itself is good, so the refusals below are the tampering's doing.
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/"))
        {
            request.Headers.Add("Cookie", $"{SessionCookie.CompleteName}={ticket}");
            using var genuine = await Bare(gateway).SendAsync(request);
            AssertRedirected(genuine);
        }

        var middle = ticket.Length / 2;
        var flipped = ticket[..middle] + (ticket[middle] == 'A' ? 'B' : 'A') + ticket[(middle + 1)..];
        await AssertNotASessionAsync(gateway, $"{SessionCookie.CompleteName}={flipped}");
        await AssertNotASessionAsync(gateway, $"pp-session={ticket}");
        await AssertNotASessionAsync(gateway, $"__Secure-pp-session={ticket}");
        await AssertNotASessionAsync(gateway, $"{SessionCookie.SecondFactorName}={ticket}");
    }

    [Fact]
    public async Task A_ticket_from_another_gateways_key_ring_is_not_a_session()
    {
        await using var issuer = new GatewayFactory("Production", sessions: _ => SessionFixtures.Accepted());
        var ticket = await CompleteTicketAsync(issuer);
        await using var other = new GatewayFactory("Production");

        await AssertNotASessionAsync(other, $"{SessionCookie.CompleteName}={ticket}");
    }

    [Fact]
    public async Task A_session_finished_with_a_second_step_is_redirected()
    {
        await using var gateway = new GatewayFactory(
            "Production",
            sessions: request => request.RequestUri!.AbsolutePath.EndsWith("/second-factor", StringComparison.Ordinal)
                ? SessionFixtures.Accepted()
                : SessionFixtures.Accepted(secondStepRequired: true));
        using var browser = await gateway.SignedInAsync(secondFactor: true);

        using var response = await browser.GetAsync("/");

        AssertRedirected(response);
    }

    [Fact]
    public async Task A_session_on_a_host_that_is_not_the_gateways_is_not_redirected()
    {
        await using var gateway = new GatewayFactory("Production", sessions: _ => SessionFixtures.Accepted());
        var ticket = await CompleteTicketAsync(gateway);

        foreach (var host in new[] { "blog.localhost", "app.localhost" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Host = host;
            request.Headers.Add("Cookie", $"{SessionCookie.CompleteName}={ticket}");

            using var response = await Bare(gateway).SendAsync(request);

            await AssertProblemAsync(response, HttpStatusCode.NotFound, GatewayApi.NotFoundCode);
        }
    }

    // ---- Revalidation ----

    [Fact]
    public async Task A_landing_page_held_from_before_sign_in_is_redirected_on_revalidation_never_reused()
    {
        await using var gateway = new GatewayFactory("Production", sessions: _ => SessionFixtures.Accepted());
        var browser = gateway.CreateBrowser();
        using var anonymous = await browser.GetAsync("/");
        var tag = anonymous.Headers.ETag!;
        var modified = anonymous.Content.Headers.LastModified!.Value;
        using var signIn = await browser.PostAsJsonAsync(Session, Credentials());
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);

        using var byTag = new HttpRequestMessage(HttpMethod.Get, "/");
        byTag.Headers.IfNoneMatch.Add(tag);
        using var tagged = await browser.SendAsync(byTag);
        using var byDate = new HttpRequestMessage(HttpMethod.Get, "/");
        byDate.Headers.IfModifiedSince = modified;
        using var dated = await browser.SendAsync(byDate);

        AssertRedirected(tagged);
        AssertRedirected(dated);
    }

    [Fact]
    public async Task A_rebuilt_document_of_the_same_length_and_time_gets_a_new_tag()
    {
        var time = new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);
        string Rebuilt(string marker) => AdminAppBuild.Create(root =>
        {
            var index = Path.Combine(root, "index.html");
            File.WriteAllText(index, $"<!doctype html><html><body><main><h1>{marker}</h1></main></body></html>");
            File.SetLastWriteTimeUtc(index, time);
        });

        await using var before = new GatewayFactory("Production", settings: Root(Rebuilt("build-one")));
        await using var after = new GatewayFactory("Production", settings: Root(Rebuilt("build-two")));
        using var first = await before.CreateBrowser().GetAsync("/");
        using var second = await after.CreateBrowser().GetAsync("/");

        Assert.Equal(first.Content.Headers.ContentLength, second.Content.Headers.ContentLength);
        Assert.Equal(first.Content.Headers.LastModified, second.Content.Headers.LastModified);
        Assert.NotEqual(first.Headers.ETag, second.Headers.ETag);
    }

    // ---- What a cacheable answer may carry ----

    [Fact]
    public async Task A_file_answer_that_renews_or_ends_a_session_is_never_publicly_cacheable()
    {
        // A fingerprinted file is cacheable at the edge (PP9.2), so an answer that sets or clears the session cookie
        // must not be: one person's cookie would be handed to the next.
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero));
        await using var gateway = new GatewayFactory("Production", sessions: _ => SessionFixtures.Accepted(), time: time);
        using var browser = await gateway.SignedInAsync();

        using var anonymousCopy = await gateway.CreateBrowser().GetAsync("/" + AdminAppBuild.Script);
        Assert.False(anonymousCopy.Headers.Contains("Set-Cookie"));
        Assert.Equal(AdminApp.ImmutableCacheControl, anonymousCopy.Headers.CacheControl?.ToString());

        var withCookie = 0;
        foreach (var hours in new[] { 5, 4, 3 })
        {
            time.Advance(TimeSpan.FromHours(hours));
            using var response = await browser.GetAsync("/" + AdminAppBuild.Script);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            if (response.SetCookie(SessionCookie.CompleteName) is not null)
            {
                withCookie++;
                Assert.False(response.Headers.CacheControl?.Public ?? false, response.Headers.CacheControl?.ToString());
                Assert.True(response.Headers.CacheControl?.NoStore, response.Headers.CacheControl?.ToString());
            }
        }

        // A renewal (at five hours) and the sign-out (at twelve) both set the cookie; neither may be cached.
        Assert.True(withCookie >= 2, $"{withCookie} answers set the session cookie.");

        // The last read was past the absolute lifetime: the cookie was cleared, on an answer nothing may keep.
        using var after = await browser.GetAsync("/");
        await AssertDocumentAsync(after, AdminAppBuild.Marker("/"));
    }

    // ---- Spellings ----

    [Theory]
    [InlineData("/pricing/", "/pricing")]
    [InlineData("/pricing;x=1", "/pricing")]
    [InlineData("/pricing;x=1/", "/pricing")]
    [InlineData("/terms/", "/terms")]
    [InlineData("//", "/")]
    [InlineData("/;x=1", "/")]
    public async Task A_public_path_spelled_another_way_is_sent_to_its_own_spelling(string path, string location)
    {
        await using var gateway = new GatewayFactory("Production");

        var context = await RawAsync(gateway, path, query: "?plan=plus");

        Assert.Equal(StatusCodes.Status301MovedPermanently, context.Response.StatusCode);
        Assert.Equal(location + "?plan=plus", context.Response.Headers.Location.ToString());
        Assert.Equal(AdminApp.DocumentCacheControl, context.Response.Headers.CacheControl.ToString());
        Assert.Equal(ContentSecurityPolicies.Document, context.Response.Headers.ContentSecurityPolicy.ToString());
    }

    [Theory]
    [InlineData("/pricing/")]
    [InlineData("/pricing;x=1")]
    [InlineData("//")]
    public async Task A_signed_in_visitor_at_another_spelling_reaches_the_dashboard_not_the_landing_page(string path)
    {
        await using var gateway = new GatewayFactory("Production", sessions: _ => SessionFixtures.Accepted());
        var ticket = await CompleteTicketAsync(gateway);

        var spelled = await RawAsync(gateway, path, cookie: $"{SessionCookie.CompleteName}={ticket}");
        var canonical = await RawAsync(gateway, spelled.Response.Headers.Location.ToString(), cookie: $"{SessionCookie.CompleteName}={ticket}");

        Assert.Equal(StatusCodes.Status301MovedPermanently, spelled.Response.StatusCode);
        Assert.Equal(StatusCodes.Status302Found, canonical.Response.StatusCode);
        Assert.Equal("/dashboard", canonical.Response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData("/dashboard/")]
    [InlineData("/nope/")]
    [InlineData("/dashboard;x=1")]
    public async Task Another_spelling_of_a_path_that_is_not_public_is_left_to_the_app(string path)
    {
        await using var gateway = new GatewayFactory("Production");

        var context = await RawAsync(gateway, path);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    // ---- Paths that are never pages ----

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    [InlineData("/health/x")]
    [InlineData("/ALIVE")]
    [InlineData("/API/nope")]
    [InlineData("/Api")]
    public async Task A_probe_or_api_path_is_never_a_page_outside_development(string path)
    {
        // Deployed, nothing maps the probes yet (PP19.2), and they skip the host check and the HTTPS redirect: a page
        // there would answer any host over plain HTTP, and a probe would read it as healthy.
        await using var gateway = new GatewayFactory("Production");

        using var https = await gateway.CreateBrowser().GetAsync(path);
        using var plain = await gateway.CreatePlain().GetAsync(path);
        using var foreign = new HttpRequestMessage(HttpMethod.Get, path);
        foreign.Headers.Host = "evil.example";
        using var elsewhere = await Bare(gateway).SendAsync(foreign);

        foreach (var response in new[] { https, plain, elsewhere })
        {
            await AssertProblemAsync(response, HttpStatusCode.NotFound, GatewayApi.NotFoundCode);
        }
    }

    [Theory]
    [InlineData("/../outside.txt")]
    [InlineData("/media/../../outside.txt")]
    [InlineData("/..%2foutside.txt")]
    [InlineData("/%2e%2e/outside.txt")]
    [InlineData("/..\\outside.txt")]
    public async Task A_path_that_leaves_the_build_is_never_served(string path)
    {
        // Sent to the server as written, so no client tidies the path first; the file beside the build is real.
        await using var gateway = new GatewayFactory("Production");

        var context = await RawAsync(gateway, path);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.DoesNotContain(AdminAppBuild.OutsideSecret, await new StreamReader(context.Response.Body).ReadToEndAsync(), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(AdminAppBuild.Root)!, AdminAppBuild.Outside)));
    }

    [Fact]
    public async Task A_signed_in_write_to_a_redirected_path_is_refused_not_redirected()
    {
        await using var gateway = new GatewayFactory("Production", sessions: _ => SessionFixtures.Accepted());
        using var browser = await gateway.SignedInAsync();

        using var response = await browser.PostAsync("/", content: null);

        await AssertProblemAsync(response, HttpStatusCode.MethodNotAllowed, GatewayApi.MethodNotAllowedCode);
    }

    [Fact]
    public void No_file_in_the_admin_apps_public_folder_counts_as_fingerprinted()
    {
        // What the app copies from public/ keeps its name, so a hash-like name there is not a promise it never changes.
        var folder = Path.GetFullPath(Path.Combine(
            DomainSource.Project.FullName, "..", "web", "projects", "pantrypress-admin", "public"));
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/'))
            .ToList();

        Assert.NotEmpty(files);
        Assert.All(files, file => Assert.False(AdminApp.IsFingerprinted(file), file));
    }

    [Theory]
    [InlineData("/pricing/", "/pricing")]
    [InlineData("/pricing;x=1;y=2", "/pricing")]
    [InlineData("/", "/")]
    [InlineData("//", "/")]
    [InlineData("/a;b/c;d/", "/a/c")]
    [InlineData("/dashboard/x", "/dashboard/x")]
    public void A_path_is_read_as_the_router_reads_it(string path, string canonical) =>
        Assert.Equal(canonical, AdminApp.CanonicalSpelling(path));

    private static Func<HttpRequestMessage, HttpResponseMessage> SessionsFor(Visitor visitor) => visitor switch
    {
        Visitor.HalfFinished => _ => SessionFixtures.Accepted(secondStepRequired: true),
        Visitor.Revoked => request => request.RequestUri!.AbsolutePath.EndsWith("/sessions/current", StringComparison.Ordinal)
            ? SessionFixtures.Refused(HttpStatusCode.BadRequest, GatewayApi.SessionEndedCode, GatewayApi.SessionEndedTitle)
            : SessionFixtures.Accepted(),
        _ => _ => SessionFixtures.Accepted(),
    };

    private static async Task<HttpClient> VisitorAsync(GatewayFactory gateway, Visitor visitor)
    {
        switch (visitor)
        {
            case Visitor.Anonymous:
                return gateway.CreateBrowser();
            case Visitor.HalfFinished:
                var browser = gateway.CreateBrowser();
                using (var signIn = await browser.PostAsJsonAsync(Session, Credentials()))
                {
                    Assert.NotNull(signIn.CookieValue(SessionCookie.SecondFactorName));
                }

                return browser;
            default:
                return await gateway.SignedInAsync();
        }
    }

    /// <summary>A client that sends only the cookies a test writes, and follows no redirect, so a 302 is seen as one.</summary>
    private static HttpClient Bare(GatewayFactory gateway) => gateway.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = GatewayFactory.AdminOrigin,
        HandleCookies = false,
        AllowAutoRedirect = false,
    });

    /// <summary>Signs in on a fresh browser and returns the complete session cookie's value.</summary>
    private static async Task<string> CompleteTicketAsync(GatewayFactory gateway)
    {
        using var signIn = await gateway.CreateBrowser().PostAsJsonAsync(Session, Credentials());
        return signIn.CookieValue(SessionCookie.CompleteName) ?? throw new InvalidOperationException("No session cookie.");
    }

    private static async Task AssertNotASessionAsync(GatewayFactory gateway, string cookie)
    {
        foreach (var path in Redirected)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("Cookie", cookie);

            using var response = await Bare(gateway).SendAsync(request);

            await AssertDocumentAsync(response, AdminAppBuild.Marker(path));
        }
    }

    /// <summary>A request handed to the server exactly as written: path, query, and cookie, over HTTPS to the gateway's host.</summary>
    private static Task<HttpContext> RawAsync(GatewayFactory gateway, string path, string query = "", string? cookie = null) =>
        gateway.Server.SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Scheme = "https";
            context.Request.Host = new HostString(GatewayFactory.AdminOrigin.Host);
            context.Request.Path = new PathString(path.Split('?')[0]);
            context.Request.QueryString = new QueryString(path.Contains('?', StringComparison.Ordinal) ? path[path.IndexOf('?', StringComparison.Ordinal)..] : query);
            if (cookie is not null)
            {
                context.Request.Headers.Cookie = cookie;
            }
        });
}
