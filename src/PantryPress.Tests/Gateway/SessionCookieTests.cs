using System.Net;
using System.Net.Http.Json;
using PantryPress.Gateway.Hosting;

namespace PantryPress.Tests.Gateway;

// PP1.6: the BFF session cookie (ACCT-002, PSEC-003, PP-D13). Tenant sites run third-party ad script on sibling
// subdomains of pantrypress.app, so these attributes are the whole defence: a __Host- cookie with no Domain cannot be
// written by a sibling, and a browser will not send it over plain HTTP. What a browser guarantees is asserted here as
// the attributes it acts on; what the gateway decides is asserted as behaviour.
public sealed class SessionCookieTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task A_sign_in_sets_one_host_only_secure_session_cookie(string environment)
    {
        await using var gateway = new GatewayFactory(environment, sessions: _ => SessionFixtures.Accepted());

        using var browser = gateway.CreateBrowser();
        using var response = await browser.PostAsJsonAsync(
            "/api/v1/account/session",
            new { email = SessionFixtures.Email, password = SessionFixtures.Passphrase });

        var cookie = response.SetCookie(SessionCookie.CompleteName);
        Assert.NotNull(cookie);
        Assert.Contains("; path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        // No Domain: that is what the __Host- prefix requires, and what keeps the cookie off sibling subdomains.
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);

        // No Expires or Max-Age: the session ends with the browser session. Its server-side life is the ticket's.
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age=", cookie, StringComparison.OrdinalIgnoreCase);

        // And nothing else: no half-finished session is left behind.
        Assert.Null(response.CookieValue(SessionCookie.SecondFactorName));
    }

    [Fact]
    public async Task A_half_finished_sign_in_sets_its_own_cookie_with_the_same_protections()
    {
        await using var gateway = new GatewayFactory(
            "Development",
            sessions: _ => SessionFixtures.Accepted(secondStepRequired: true));

        using var browser = gateway.CreateBrowser();
        using var response = await browser.PostAsJsonAsync(
            "/api/v1/account/session",
            new { email = SessionFixtures.Email, password = SessionFixtures.Passphrase });

        var pending = response.SetCookie(SessionCookie.SecondFactorName);
        Assert.NotNull(pending);
        Assert.StartsWith("__Host-", SessionCookie.SecondFactorName, StringComparison.Ordinal);
        Assert.Contains("; path=/", pending, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; secure", pending, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", pending, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; samesite=lax", pending, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", pending, StringComparison.OrdinalIgnoreCase);

        // A password alone is not a session.
        Assert.Null(response.CookieValue(SessionCookie.CompleteName));
    }

    [Fact]
    public async Task Signing_out_clears_both_cookies()
    {
        await using var gateway = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());
        using var browser = await gateway.SignedInAsync();

        using var response = await browser.DeleteAsync("/api/v1/account/session");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(response.CookieValue(SessionCookie.CompleteName));
        Assert.Null(response.CookieValue(SessionCookie.SecondFactorName));
        Assert.NotNull(response.SetCookie(SessionCookie.CompleteName));
        Assert.NotNull(response.SetCookie(SessionCookie.SecondFactorName));

        using var after = await browser.GetAsync("/api/v1/account/session");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task The_same_ticket_under_another_cookie_name_is_not_a_session()
    {
        await using var gateway = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());
        var ticket = await TicketAsync(gateway);

        // Cookie tossing, as a browser would allow it: a sibling subdomain cannot write __Host-pp-session at all, but
        // it can write pp-session or __Secure-pp-session on the parent domain. The gateway reads one name only.
        foreach (var name in new[] { "pp-session", "__Secure-pp-session", "__Host-pp-session-x" })
        {
            using var client = gateway.CreateBare();
            client.DefaultRequestHeaders.Add("Cookie", $"{name}={ticket}");
            using var response = await client.GetAsync("/api/v1/account/session");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_ticket_protected_by_another_key_ring_is_not_a_session()
    {
        await using var mine = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());
        await using var theirs = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());
        var ticket = await TicketAsync(theirs);

        using var client = mine.CreateBare();
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.CompleteName}={ticket}");

        using var session = await client.GetAsync("/api/v1/account/session");
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);

        // And nothing of it reaches the API: a ticket this gateway cannot read names nobody.
        using var forwarded = await client.GetAsync("/api/v1/account/two-factor");
        Assert.Equal(HttpStatusCode.OK, forwarded.StatusCode);
        var request = Assert.Single(mine.Forwarded);
        Assert.Null(request.Header(SessionHeaders.Account));
        Assert.Null(request.Header(SessionHeaders.Secret));
    }

    [Fact]
    public async Task A_session_cookie_sent_to_a_tenant_host_gets_no_further_than_the_host_check()
    {
        // Front Door routes a site host to the public site, never here, and the __Host- prefix means a sibling never
        // had this cookie to begin with. Since PP1.8 one arriving anyway does not even reach the cookie: a tenant
        // host is not one of the gateway's own, so it gets the neutral 404 an unknown host gets everywhere. (That a
        // ticket this gateway did not write names nobody is proven on its own host, above.)
        await using var gateway = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());
        await using var sibling = new GatewayFactory("Development", sessions: _ => SessionFixtures.Accepted());

        using var client = gateway.CreateBare();
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.CompleteName}={await TicketAsync(sibling)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/account/session");
        request.Headers.Host = "cook.localhost";

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(GatewayApi.NotFoundCode, await response.CodeAsync());
    }

    private static async Task<string> TicketAsync(GatewayFactory gateway)
    {
        using var browser = gateway.CreateBrowser();
        using var response = await browser.PostAsJsonAsync(
            "/api/v1/account/session",
            new { email = SessionFixtures.Email, password = SessionFixtures.Passphrase });
        return Assert.IsType<string>(response.CookieValue(SessionCookie.CompleteName));
    }
}
