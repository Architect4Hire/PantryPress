using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PantryPress.Gateway.Hosting;

namespace PantryPress.Tests.Gateway;

// PP1.12 (audit findings): a session ends when its owner signs out - not only the browser's copy of it - and ends
// twelve hours after sign-in however busy it is kept; the current-session route reports what the API says now, not what
// the cookie said at sign-in; and a failure to reach the API is the gateway's own 502, never an unhandled fault.
public sealed class SessionRevocationTests
{
    private const string Session = "/api/v1/account/session";
    private const string Forwarded = "/api/v1/account/two-factor";

    private static readonly Dictionary<string, string> SignIn = new()
    {
        ["email"] = SessionFixtures.Email,
        ["password"] = SessionFixtures.Passphrase,
    };

    [Fact]
    public async Task Each_session_gets_an_id_of_its_own_and_forwards_it()
    {
        await using var gateway = new GatewayFactory("Development", sessions: Api());
        using var first = await gateway.SignedInAsync();
        using var second = await gateway.SignedInAsync();

        using var one = await first.GetAsync(Forwarded);
        using var two = await second.GetAsync(Forwarded);

        var ids = gateway.Forwarded.Select(r => r.Header(SessionHeaders.SessionId)).ToList();
        Assert.Equal(2, ids.Count);
        Assert.All(ids, id => Assert.Matches("^[A-Za-z0-9_-]{43}$", id));
        Assert.NotEqual(ids[0], ids[1]);
    }

    [Fact]
    public async Task Signing_out_revokes_the_session_at_the_api_and_clears_the_cookies()
    {
        await using var gateway = new GatewayFactory("Development", sessions: Api());
        using var browser = await gateway.SignedInAsync();
        using var used = await browser.GetAsync(Forwarded);
        var sessionId = Assert.Single(gateway.Forwarded).Header(SessionHeaders.SessionId);

        using var signedOut = await browser.DeleteAsync(Session);

        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        var end = Assert.Single(gateway.SessionCalls, c => Path(c.Uri).EndsWith("/sessions/end", StringComparison.Ordinal));
        Assert.Equal(TestSecrets.GatewaySecret, end.Secret);
        using var body = JsonDocument.Parse(end.Body);
        Assert.Equal(SessionFixtures.Account, body.RootElement.GetProperty("accountId").GetGuid());
        Assert.Equal(sessionId, body.RootElement.GetProperty("sessionId").GetString());
        Assert.StartsWith($"{SessionCookie.CompleteName}=;", signedOut.SetCookie(SessionCookie.CompleteName), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Signing_out_clears_the_cookies_even_when_the_api_cannot_be_asked()
    {
        // The person asked to be signed out on this device, and is; the missed revocation is logged as an error.
        await using var gateway = new GatewayFactory(
            "Development",
            sessions: request => Path(request).EndsWith("/sessions/end", StringComparison.Ordinal)
                ? throw new HttpRequestException("Connection refused.")
                : SessionFixtures.Accepted());
        using var browser = await gateway.SignedInAsync();

        using var signedOut = await browser.DeleteAsync(Session);

        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        Assert.StartsWith($"{SessionCookie.CompleteName}=;", signedOut.SetCookie(SessionCookie.CompleteName), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_session_ends_twelve_hours_after_sign_in_however_busy_it_is_kept()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero));
        await using var gateway = new GatewayFactory("Development", sessions: Api(), time: time);
        using var browser = await gateway.SignedInAsync();

        // Used every four hours, well inside the eight-hour sliding window, so only the absolute lifetime can end it.
        for (var hours = 4; hours < 12; hours += 4)
        {
            time.Advance(TimeSpan.FromHours(4));
            using var stillOn = await browser.GetAsync(Forwarded);
            Assert.NotNull(gateway.Forwarded.Last().Header(SessionHeaders.Account));
        }

        time.Advance(TimeSpan.FromHours(4));
        using var over = await browser.GetAsync(Forwarded);

        Assert.Null(gateway.Forwarded.Last().Header(SessionHeaders.Account));
        Assert.Null(gateway.Forwarded.Last().Header(SessionHeaders.SessionId));
        using var current = await browser.GetAsync(Session);
        Assert.Equal(HttpStatusCode.Unauthorized, current.StatusCode);
    }

    [Fact]
    public async Task The_current_session_is_what_the_api_says_now()
    {
        // Signed in before the address was confirmed; confirmed since. The cookie still says "unconfirmed".
        await using var gateway = new GatewayFactory(
            "Development",
            sessions: Api(signInEmailVerified: false, currentEmailVerified: true));
        using var browser = await gateway.SignedInAsync();

        using var response = await browser.GetAsync(Session);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("complete", view.GetProperty("stage").GetString());
        Assert.True(view.GetProperty("emailVerified").GetBoolean());
        var current = Assert.Single(gateway.SessionCalls, c => Path(c.Uri).EndsWith("/sessions/current", StringComparison.Ordinal));
        Assert.Equal(TestSecrets.GatewaySecret, current.Secret);
        using var body = JsonDocument.Parse(current.Body);
        Assert.Equal(SessionFixtures.Stamp, body.RootElement.GetProperty("securityStamp").GetString());
        Assert.Matches("^[A-Za-z0-9_-]{43}$", body.RootElement.GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task A_session_the_api_says_has_ended_is_cleared_and_reported_as_signed_out()
    {
        await using var gateway = new GatewayFactory(
            "Development",
            sessions: request => Path(request).EndsWith("/sessions/current", StringComparison.Ordinal)
                ? SessionFixtures.Refused(HttpStatusCode.BadRequest, GatewayApi.SessionEndedCode, GatewayApi.SessionEndedTitle)
                : SessionFixtures.Accepted());
        using var browser = await gateway.SignedInAsync();

        using var response = await browser.GetAsync(Session);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(GatewayApi.SessionEndedCode, problem.GetProperty("code").GetString());
        Assert.Equal(GatewayApi.SessionEndedTitle, problem.GetProperty("title").GetString());
        Assert.StartsWith($"{SessionCookie.CompleteName}=;", response.SetCookie(SessionCookie.CompleteName), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("connection")]
    [InlineData("timeout")]
    public async Task An_api_that_cannot_be_reached_is_the_gateways_own_bad_gateway(string failure)
    {
        await using var gateway = new GatewayFactory(
            "Development",
            sessions: _ => failure == "timeout"
                ? throw new TaskCanceledException("The request timed out.")
                : throw new HttpRequestException("Connection refused."));

        using var browser = gateway.CreateBrowser();
        using var response = await browser.PostAsJsonAsync(Session, SignIn);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(GatewayApi.ServerErrorCode, problem.GetProperty("code").GetString());
        Assert.Null(response.SetCookie(SessionCookie.CompleteName));
    }

    [Fact]
    public async Task The_current_session_is_a_bad_gateway_when_the_api_cannot_be_asked()
    {
        await using var gateway = new GatewayFactory(
            "Development",
            sessions: request => Path(request).EndsWith("/sessions/current", StringComparison.Ordinal)
                ? throw new HttpRequestException("Connection refused.")
                : SessionFixtures.Accepted());
        using var browser = await gateway.SignedInAsync();

        using var response = await browser.GetAsync(Session);

        // Not "signed in" on the cookie's word alone, and not "signed out" either: we could not ask.
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Null(response.SetCookie(SessionCookie.CompleteName));
    }

    [Fact]
    public async Task A_sign_in_that_can_no_longer_be_finished_takes_its_cookie_with_it()
    {
        await using var gateway = new GatewayFactory(
            "Development",
            sessions: request => Path(request).EndsWith("/sessions/second-factor", StringComparison.Ordinal)
                ? SessionFixtures.Refused(HttpStatusCode.BadRequest, SessionEndpoints.SignInExpiredCode, "Start again.")
                : SessionFixtures.Accepted(secondStepRequired: true));
        using var browser = gateway.CreateBrowser();
        using var started = await browser.PostAsJsonAsync(Session, SignIn);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        using var expired = await browser.PostAsJsonAsync($"{Session}/second-factor", new { code = "123456" });

        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.StartsWith($"{SessionCookie.SecondFactorName}=;", expired.SetCookie(SessionCookie.SecondFactorName), StringComparison.Ordinal);
        using var current = await browser.GetAsync(Session);
        Assert.Equal(HttpStatusCode.Unauthorized, current.StatusCode);
    }

    [Fact]
    public async Task A_wrong_code_leaves_the_half_finished_sign_in_in_place()
    {
        await using var gateway = new GatewayFactory(
            "Development",
            sessions: request => Path(request).EndsWith("/sessions/second-factor", StringComparison.Ordinal)
                ? SessionFixtures.Refused(HttpStatusCode.BadRequest, "invalid-code", "Try again.")
                : SessionFixtures.Accepted(secondStepRequired: true));
        using var browser = gateway.CreateBrowser();
        using var started = await browser.PostAsJsonAsync(Session, SignIn);

        using var wrong = await browser.PostAsJsonAsync($"{Session}/second-factor", new { code = "123456" });

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Null(wrong.SetCookie(SessionCookie.SecondFactorName));
    }

    // ApiService's internal surface as the gateway meets it: sign-in, the current-session check, and sign-out.
    private static Func<HttpRequestMessage, HttpResponseMessage> Api(
        bool signInEmailVerified = true,
        bool currentEmailVerified = true) => request =>
    {
        var path = Path(request);
        if (path.EndsWith("/sessions/end", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (path.EndsWith("/sessions/current", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    accountId = SessionFixtures.Account,
                    email = SessionFixtures.Email,
                    emailVerified = currentEmailVerified,
                    secondFactorVerified = false,
                }),
            };
        }

        return SessionFixtures.Accepted(emailVerified: signInEmailVerified);
    };

    private static string Path(HttpRequestMessage request) => Path(request.RequestUri!);

    private static string Path(Uri uri) => uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString;
}
