using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using PantryPress.Gateway.Hosting;
using PantryPress.Tests.Persistence;

namespace PantryPress.Tests.Gateway;

// PP1.6 end to end: a real sign-in through the real gateway into the real ApiService on SQL (ACCT-002, PSEC-003).
// Nothing is faked here - the cookie, the internal session call, the forwarded identity, and the API's own session
// scheme are all the shipping code. It shares the running system with the other integration tests (MigratedDatabase).
[Collection(MigratedDatabase.CollectionName)]
public sealed class SessionEndToEndTests(MigratedDatabase system)
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Signing_in_through_the_gateway_reaches_a_signed_in_route()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await system.App.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", timeout.Token);
        await system.App.ResourceNotifications.WaitForResourceHealthyAsync("gateway", timeout.Token);
        // Over TLS, because that is the only way the gateway is reached (PP1.6) and the only way its Secure cookies -
        // the session's and the antiforgery token's - come back. The certificate is not checked: what is under test is
        // the gateway's session, not whether this machine trusts the development certificate. The Host is set to
        // localhost, the local apex the gateway answers on (PP-D19).
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,

            // Every cookie below is sent by hand, so that what each request carries is what the test says it carries.
            UseCookies = false,
        };
        using var client = new HttpClient(handler) { BaseAddress = system.App.GetEndpoint("gateway", "https") };
        var host = $"localhost:{client.BaseAddress!.Port}";
        var email = $"cook-{Guid.NewGuid():N}@example.test";
        const string passphrase = "a long kitchen phrase";

        // The admin app's first call: it comes back 401 with nothing signed in, and with the antiforgery token every
        // write below has to carry (PP1.7).
        using var bootstrap = await SendAsync(client, host, HttpMethod.Get, "/api/v1/account/session", timeout.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, bootstrap.StatusCode);
        var cookieToken = Cookie(bootstrap, GatewayAntiforgery.CookieName);
        var requestToken = Cookie(bootstrap, GatewayAntiforgery.TokenCookieName);
        Assert.NotNull(cookieToken);
        Assert.NotNull(requestToken);
        var tokens = $"{cookieToken}; {requestToken}";
        var token = requestToken![(GatewayAntiforgery.TokenCookieName.Length + 1)..];

        // Signing in is all an account needs; confirming the address is what publishing needs (ACCT-001).
        using var registered = await SendAsync(
            client,
            host,
            HttpMethod.Post,
            "/api/v1/account/register",
            timeout.Token,
            body: new { email, password = passphrase },
            cookie: tokens,
            token: token);
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);

        // And without it the same write is refused before it reaches the API at all.
        using var untokened = await SendAsync(
            client,
            host,
            HttpMethod.Post,
            "/api/v1/account/register",
            timeout.Token,
            body: new { email, password = passphrase });
        Assert.Equal(HttpStatusCode.BadRequest, untokened.StatusCode);

        using var signedIn = await SendAsync(
            client,
            host,
            HttpMethod.Post,
            "/api/v1/account/session",
            timeout.Token,
            body: new { email, password = passphrase },
            cookie: tokens,
            token: token);

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        using var view = JsonDocument.Parse(await signedIn.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal("complete", view.RootElement.GetProperty("stage").GetString());
        Assert.False(view.RootElement.GetProperty("emailVerified").GetBoolean());

        // What the browser can read names the account and nothing that could act for it: no stamp, no token (ACCT-002).
        Assert.Equal(
            ["accountId", "email", "emailVerified", "secondFactorVerified", "stage"],
            view.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));

        // The gateway holds the session; the browser holds a cookie and no token. A real browser is sent here over
        // HTTPS (at the apex, localhost, locally), which is what a Secure cookie needs; this test replays the cookie by hand.
        var session = Cookie(signedIn, SessionCookie.CompleteName);
        Assert.NotNull(session);

        using var signedInRoute = await SendAsync(
            client,
            host,
            HttpMethod.Get,
            "/api/v1/account/two-factor",
            timeout.Token,
            cookie: session);

        Assert.Equal(HttpStatusCode.OK, signedInRoute.StatusCode);
        using var status = JsonDocument.Parse(await signedInRoute.Content.ReadAsStringAsync(timeout.Token));
        Assert.False(status.RootElement.GetProperty("isEnabled").GetBoolean());

        // Without the cookie the same route is the API's own 401, so the session is what let the first one through.
        using var anonymous = await SendAsync(client, host, HttpMethod.Get, "/api/v1/account/two-factor", timeout.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var current = await SendAsync(
            client,
            host,
            HttpMethod.Get,
            "/api/v1/account/session",
            timeout.Token,
            cookie: session);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);

        using var signedOut = await SendAsync(
            client,
            host,
            HttpMethod.Delete,
            "/api/v1/account/session",
            timeout.Token,
            cookie: $"{session}; {tokens}",
            token: token);

        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);

        // The browser's copy is gone...
        Assert.NotNull(signedOut.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(value => value.StartsWith($"{SessionCookie.CompleteName}=;", StringComparison.Ordinal)));

        // ...and so is the session itself (PP1.12): a copy of the cookie, kept from before the sign-out, reaches nothing.
        using var replayed = await SendAsync(
            client,
            host,
            HttpMethod.Get,
            "/api/v1/account/two-factor",
            timeout.Token,
            cookie: session);
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);
        using var replayedSession = await SendAsync(
            client,
            host,
            HttpMethod.Get,
            "/api/v1/account/session",
            timeout.Token,
            cookie: session);
        Assert.Equal(HttpStatusCode.Unauthorized, replayedSession.StatusCode);
    }

    private static string? Cookie(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return null;
        }

        var header = values.FirstOrDefault(value => value.StartsWith(name + "=", StringComparison.Ordinal));
        return header?.Split(';')[0];
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string host,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        object? body = null,
        string? cookie = null,
        string? token = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Host = host;
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        if (token is not null)
        {
            request.Headers.Add(GatewayAntiforgery.HeaderName, token);
        }

        return await client.SendAsync(request, cancellationToken);
    }
}
