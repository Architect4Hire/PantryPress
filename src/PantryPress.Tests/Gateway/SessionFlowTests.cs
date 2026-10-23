using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PantryPress.Gateway.Hosting;

namespace PantryPress.Tests.Gateway;

// PP1.6: sign-in, the second step, the current session, and sign-out, as the admin app sees them. The rules all live
// in the Accounts facades; what is tested here is that the gateway asks, believes the answer, and turns it into a
// cookie - and that a failure reaches the admin app in the API's own words.
public sealed class SessionFlowTests
{
    [Fact]
    public async Task A_sign_in_asks_the_api_behind_the_shared_secret()
    {
        await using var gateway = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());

        using var browser = gateway.CreateBrowser();
        using var response = await browser.PostAsJsonAsync(
            "/api/v1/account/session",
            new { email = SessionFixtures.Email, password = SessionFixtures.Passphrase });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var call = Assert.Single(gateway.SessionCalls);
        Assert.Equal("/api/internal/v1/sessions/credentials", call.Uri.AbsolutePath);
        Assert.Equal(TestSecrets.GatewaySecret, call.Secret);
        Assert.Contains(SessionFixtures.Email, call.Body, StringComparison.Ordinal);

        // The sign-in route is never forwarded: the gateway owns it (gateway.md).
        Assert.Empty(gateway.Forwarded);

        var view = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("complete", view.GetProperty("stage").GetString());
        Assert.Equal(SessionFixtures.Account, view.GetProperty("accountId").GetGuid());
        Assert.Equal(SessionFixtures.Email, view.GetProperty("email").GetString());
        Assert.True(view.GetProperty("emailVerified").GetBoolean());

        // Nothing the browser receives is a token or a stamp (ACCT-002).
        Assert.DoesNotContain("securityStamp", view.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SessionFixtures.Stamp, view.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "invalid-credentials")]
    [InlineData(HttpStatusCode.TooManyRequests, "too-many-attempts")]
    public async Task A_refused_sign_in_is_relayed_in_the_apis_own_words_and_starts_nothing(
        HttpStatusCode status,
        string code)
    {
        await using var gateway = new GatewayFactory(
            "Development",
            sessions: _ => SessionFixtures.Refused(status, code, "No."));

        using var browser = gateway.CreateBrowser();
        using var response = await browser.PostAsJsonAsync(
            "/api/v1/account/session",
            new { email = SessionFixtures.Email, password = "wrong passphrase here" });

        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Null(response.SetCookie(SessionCookie.CompleteName));
        Assert.Null(response.SetCookie(SessionCookie.SecondFactorName));
    }

    [Fact]
    public async Task An_account_with_two_step_sign_in_owes_a_second_step_before_it_can_do_anything()
    {
        await using var gateway = new GatewayFactory(
            "Development",
            sessions: _ => SessionFixtures.Accepted(secondStepRequired: true));

        using var browser = gateway.CreateBrowser();
        using var signedIn = await browser.PostAsJsonAsync(
            "/api/v1/account/session",
            new { email = SessionFixtures.Email, password = SessionFixtures.Passphrase });
        var view = await signedIn.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("second-factor", view.GetProperty("stage").GetString());

        // The half-finished session reaches the API's routes not at all.
        using var refused = await browser.GetAsync("/api/v1/account/two-factor");
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(SessionIdentity.SecondFactorRequiredCode, problem.GetProperty("code").GetString());
        Assert.Empty(gateway.Forwarded);

        // And it is what the current-session route reports.
        using var current = await browser.GetAsync("/api/v1/account/session");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        var currentView = await current.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("second-factor", currentView.GetProperty("stage").GetString());
        Assert.False(currentView.GetProperty("secondFactorVerified").GetBoolean());
    }

    [Fact]
    public async Task The_second_step_completes_the_session_and_replaces_the_half_finished_cookie()
    {
        var owesSecondStep = true;
        await using var gateway = new GatewayFactory("Development", sessions: _ =>
        {
            var answer = SessionFixtures.Accepted(secondStepRequired: owesSecondStep);
            owesSecondStep = false;
            return answer;
        });

        using var browser = gateway.CreateBrowser();
        using var started = await browser.PostAsJsonAsync(
            "/api/v1/account/session",
            new { email = SessionFixtures.Email, password = SessionFixtures.Passphrase });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        using var completed = await browser.PostAsJsonAsync(
            "/api/v1/account/session/second-factor",
            new { code = "123456" });

        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var view = await completed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("complete", view.GetProperty("stage").GetString());
        Assert.True(view.GetProperty("secondFactorVerified").GetBoolean());
        Assert.NotNull(completed.CookieValue(SessionCookie.CompleteName));
        Assert.Null(completed.CookieValue(SessionCookie.SecondFactorName));

        var call = gateway.SessionCalls.Last();
        Assert.Equal("/api/internal/v1/sessions/second-factor", call.Uri.AbsolutePath);
        Assert.Contains(SessionFixtures.Account.ToString(), call.Body, StringComparison.OrdinalIgnoreCase);

        // The completed session passed the second step, and the API is told so.
        using var forwarded = await browser.GetAsync("/api/v1/account/two-factor");
        Assert.Equal(HttpStatusCode.OK, forwarded.StatusCode);
        Assert.Equal(SessionHeaders.True, Assert.Single(gateway.Forwarded).Header(SessionHeaders.SecondFactor));
    }

    [Fact]
    public async Task A_wrong_code_is_relayed_and_leaves_the_half_finished_session_alone()
    {
        var first = true;
        await using var gateway = new GatewayFactory("Development", sessions: _ =>
        {
            if (first)
            {
                first = false;
                return SessionFixtures.Accepted(secondStepRequired: true);
            }

            return SessionFixtures.Refused(code: "invalid-code", title: "That code isn't right.");
        });

        using var browser = gateway.CreateBrowser();
        using var started = await browser.PostAsJsonAsync(
            "/api/v1/account/session",
            new { email = SessionFixtures.Email, password = SessionFixtures.Passphrase });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        using var refused = await browser.PostAsJsonAsync(
            "/api/v1/account/session/second-factor",
            new { code = "000000" });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid-code", problem.GetProperty("code").GetString());
        Assert.Null(refused.SetCookie(SessionCookie.CompleteName));

        // Still half-finished, so another code can be tried.
        using var current = await browser.GetAsync("/api/v1/account/session");
        var view = await current.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("second-factor", view.GetProperty("stage").GetString());
    }

    [Fact]
    public async Task There_is_nothing_to_finish_without_a_half_finished_session()
    {
        await using var gateway = new GatewayFactory("Development");

        using var browser = gateway.CreateBrowser();
        using var response = await browser.PostAsJsonAsync(
            "/api/v1/account/session/second-factor",
            new { code = "123456" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(GatewayApi.UnauthorizedCode, problem.GetProperty("code").GetString());

        // The API is never asked to check a code for a session that does not exist.
        Assert.Empty(gateway.SessionCalls);
    }

    [Fact]
    public async Task The_current_session_is_a_problem_401_when_there_is_none()
    {
        await using var gateway = new GatewayFactory("Development");

        using var browser = gateway.CreateBrowser();
        using var response = await browser.GetAsync("/api/v1/account/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(GatewayApi.UnauthorizedCode, problem.GetProperty("code").GetString());
        Assert.Equal(GatewayApi.UnauthorizedTitle, problem.GetProperty("title").GetString());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Signing_out_without_a_session_is_still_a_clean_sign_out()
    {
        await using var gateway = new GatewayFactory("Development");

        using var browser = gateway.CreateBrowser();
        using var response = await browser.DeleteAsync("/api/v1/account/session");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(gateway.Forwarded);
    }

    [Fact]
    public async Task A_half_finished_sign_in_that_is_left_too_long_cannot_be_finished()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
            new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
        await using var gateway = new GatewayFactory(
            "Development",
            sessions: _ => SessionFixtures.Accepted(secondStepRequired: true),
            time: time);
        using var browser = gateway.CreateBrowser();
        using var started = await browser.PostAsJsonAsync(
            "/api/v1/account/session",
            new { email = SessionFixtures.Email, password = SessionFixtures.Passphrase });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        // A right password is not a standing invitation: the half-finished session lasts minutes, not hours.
        time.Advance(SessionCookie.SecondFactorLifetime + TimeSpan.FromSeconds(1));
        using var tooLate = await browser.PostAsJsonAsync(
            "/api/v1/account/session/second-factor",
            new { code = "123456" });

        Assert.Equal(HttpStatusCode.Unauthorized, tooLate.StatusCode);
        var problem = await tooLate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(GatewayApi.UnauthorizedCode, problem.GetProperty("code").GetString());

        // No code was ever offered to the API, so nothing was counted against the account.
        Assert.Single(gateway.SessionCalls);
    }

    [Fact]
    public async Task The_two_session_routes_agree_about_a_verified_email()
    {
        await using var gateway = new GatewayFactory(
            "Development",
            sessions: _ => SessionFixtures.Accepted(secondStepRequired: true, emailVerified: true));

        using var browser = gateway.CreateBrowser();
        using var signedIn = await browser.PostAsJsonAsync(
            "/api/v1/account/session",
            new { email = SessionFixtures.Email, password = SessionFixtures.Passphrase });
        using var current = await browser.GetAsync("/api/v1/account/session");

        var started = await signedIn.Content.ReadFromJsonAsync<JsonElement>();
        var read = await current.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(started.GetProperty("emailVerified").GetBoolean());
        Assert.Equal(
            started.GetProperty("emailVerified").GetBoolean(),
            read.GetProperty("emailVerified").GetBoolean());
    }

    [Fact]
    public async Task A_locked_account_keeps_the_apis_retry_after()
    {
        await using var gateway = new GatewayFactory("Development", sessions: _ =>
        {
            var refused = SessionFixtures.Refused(HttpStatusCode.TooManyRequests, "too-many-attempts", "Wait.");
            refused.Headers.Add("Retry-After", "900");
            return refused;
        });

        using var browser = gateway.CreateBrowser();
        using var response = await browser.PostAsJsonAsync(
            "/api/v1/account/session",
            new { email = SessionFixtures.Email, password = SessionFixtures.Passphrase });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);

        // The plain-language "wait a quarter of an hour" keeps its machine-readable half.
        Assert.Equal("900", Assert.Single(response.Headers.GetValues("Retry-After")));
    }

    [Fact]
    public async Task An_answer_that_is_not_the_bloggers_business_becomes_the_gateways_own_failure()
    {
        // A 404 from the internal surface means the shared secret does not match - an operator's problem. The person
        // signing in gets a plain "something went wrong on our side", not "we couldn't find that" on a sign-in form.
        await using var gateway = new GatewayFactory(
            "Development",
            sessions: _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        using var browser = gateway.CreateBrowser();
        using var response = await browser.PostAsJsonAsync(
            "/api/v1/account/session",
            new { email = SessionFixtures.Email, password = SessionFixtures.Passphrase });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(GatewayApi.ServerErrorCode, problem.GetProperty("code").GetString());
        Assert.Equal(GatewayApi.ServerErrorTitle, problem.GetProperty("title").GetString());
        Assert.Null(response.SetCookie(SessionCookie.CompleteName));
    }

    [Fact]
    public async Task Session_answers_are_never_cached()
    {
        await using var gateway = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());

        using var browser = gateway.CreateBrowser();
        using var signedIn = await browser.PostAsJsonAsync(
            "/api/v1/account/session",
            new { email = SessionFixtures.Email, password = SessionFixtures.Passphrase });
        using var current = await browser.GetAsync("/api/v1/account/session");
        using var signedOut = await browser.DeleteAsync("/api/v1/account/session");

        foreach (var response in new[] { signedIn, current, signedOut })
        {
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        }
    }
}
