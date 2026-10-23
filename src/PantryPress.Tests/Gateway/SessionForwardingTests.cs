using System.Net;
using System.Net.Http.Json;
using PantryPress.Gateway.Hosting;

namespace PantryPress.Tests.Gateway;

// PP1.6: what the API is told about the caller, and what it is never told (gateway.md). The identity headers carry no
// authority of their own - ApiService rebuilds the caller from the database - but they must still be the gateway's
// word alone, so anything a client sends that looks like them is taken away first.
public sealed class SessionForwardingTests
{
    private const string Route = "/api/v1/account/two-factor";

    [Fact]
    public async Task A_complete_session_forwards_the_account_its_stamp_and_the_secret()
    {
        await using var gateway = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());
        using var browser = await gateway.SignedInAsync();

        using var response = await browser.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = Assert.Single(gateway.Forwarded);
        Assert.Equal(TestSecrets.GatewaySecret, forwarded.Header(SessionHeaders.Secret));
        Assert.Equal(SessionFixtures.Account.ToString("D"), forwarded.Header(SessionHeaders.Account));
        Assert.Equal(SessionFixtures.Stamp, forwarded.Header(SessionHeaders.Stamp));

        // This account has no second step, so the session does not claim to have passed one.
        Assert.Null(forwarded.Header(SessionHeaders.SecondFactor));
    }

    [Fact]
    public async Task The_session_cookie_is_never_forwarded()
    {
        await using var gateway = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());
        using var browser = await gateway.SignedInAsync();

        using var response = await browser.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(Assert.Single(gateway.Forwarded).Header("Cookie"));
    }

    [Fact]
    public async Task An_anonymous_request_is_forwarded_with_no_identity_at_all()
    {
        await using var gateway = new GatewayFactory("Development");

        using var browser = gateway.CreateBrowser();
        using var response = await browser.PostAsJsonAsync("/api/v1/account/register", new { email = "cook@example.test" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = Assert.Single(gateway.Forwarded);
        foreach (var header in SessionHeaders.Request)
        {
            Assert.Null(forwarded.Header(header));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Identity_headers_a_client_sends_are_taken_away(bool signedIn)
    {
        await using var gateway = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());
        using var client = signedIn ? await gateway.SignedInAsync() : gateway.CreateBrowser();

        using var request = new HttpRequestMessage(HttpMethod.Get, Route);
        request.Headers.Add(SessionHeaders.Secret, TestSecrets.WrongGatewaySecret);
        request.Headers.Add(SessionHeaders.Account, Guid.NewGuid().ToString("D"));
        request.Headers.Add(SessionHeaders.Stamp, "FORGED");
        request.Headers.Add(SessionHeaders.SecondFactor, SessionHeaders.True);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = Assert.Single(gateway.Forwarded);
        Assert.NotEqual(TestSecrets.WrongGatewaySecret, forwarded.Header(SessionHeaders.Secret));
        Assert.NotEqual("FORGED", forwarded.Header(SessionHeaders.Stamp));

        if (signedIn)
        {
            // Replaced by the gateway's own word, not appended to the client's.
            Assert.Equal(SessionFixtures.Account.ToString("D"), forwarded.Header(SessionHeaders.Account));
            Assert.Equal(TestSecrets.GatewaySecret, forwarded.Header(SessionHeaders.Secret));
            Assert.Null(forwarded.Header(SessionHeaders.SecondFactor));
        }
        else
        {
            foreach (var header in SessionHeaders.Request)
            {
                Assert.Null(forwarded.Header(header));
            }
        }
    }

    [Fact]
    public async Task The_api_saying_the_session_ended_clears_the_cookie_and_the_header_never_reaches_the_browser()
    {
        await using var gateway = new GatewayFactory(
            "Development",
            sessions: _ => SessionFixtures.Accepted(),
            forward: () =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                response.Headers.Add(SessionHeaders.Ended, SessionHeaders.EndedValue);
                return response;
            });
        using var browser = await gateway.SignedInAsync();

        using var response = await browser.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains(SessionHeaders.Ended));
        Assert.NotNull(response.SetCookie(SessionCookie.CompleteName));
        Assert.Null(response.CookieValue(SessionCookie.CompleteName));

        using var after = await browser.GetAsync("/api/v1/account/session");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task A_request_that_rotated_the_stamp_keeps_its_own_session_against_the_new_one()
    {
        await using var gateway = new GatewayFactory(
            "Development",
            sessions: _ => SessionFixtures.Accepted(),
            forward: () =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.NoContent);
                response.Headers.Add(SessionHeaders.NewStamp, SessionFixtures.RotatedStamp);
                return response;
            });
        using var browser = await gateway.SignedInAsync();

        using var changed = await browser.PostAsJsonAsync("/api/v1/account/password/change", new { currentPassword = "x", newPassword = "y" });

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.False(changed.Headers.Contains(SessionHeaders.NewStamp));
        Assert.NotNull(changed.CookieValue(SessionCookie.CompleteName));

        // The session carries on, now bound to the stamp the change produced; the account's other sessions are over.
        using var next = await browser.GetAsync(Route);
        Assert.Equal(SessionFixtures.RotatedStamp, gateway.Forwarded.Last().Header(SessionHeaders.Stamp));
        Assert.Equal(HttpStatusCode.NoContent, next.StatusCode);
    }

    [Fact]
    public async Task The_admin_app_route_gets_no_identity_of_ours()
    {
        await using var gateway = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());
        using var browser = await gateway.SignedInAsync();

        using var response = await browser.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = Assert.Single(gateway.Forwarded);
        Assert.Equal(GatewayFactory.AdminDestination, forwarded.Uri.Host);
        Assert.Null(forwarded.Header(SessionHeaders.Account));
        Assert.Null(forwarded.Header(SessionHeaders.Secret));

        // Nor the session cookie: it is the gateway's own business, and the dev server has no use for it (PP0.7).
        Assert.Null(forwarded.Header("Cookie"));
    }
}
