using System.Net;
using System.Net.Http.Headers;
using PantryPress.ServiceDefaults.EdgeHardening;

namespace PantryPress.Tests.Site;

// PP1.8 on the public tier: the headers a reader's browser meets, and a body ceiling sized for what a reader sends
// (PSEC-002, public-endpoints.md). The per-site policy that replaces the placeholder below is PP9's.
public sealed class SiteEdgeHardeningTests
{
    [Fact]
    public async Task A_readers_answer_carries_the_public_headers()
    {
        // Any reader path: the public tier has no endpoints of its own yet, and a 404 is as much an answer to a
        // browser as a page is. Probes are the exception and are asserted separately.
        await using var site = new SiteFactory("Development");

        using var response = await site.CreateReader().GetAsync("/recipes/sourdough");

        Assert.Equal("nosniff", Header(response, SecurityHeaders.ContentTypeOptions));
        Assert.Equal("DENY", Header(response, SecurityHeaders.FrameOptions));

        // A blogger's outbound links are credited back by the referrer, so the origin travels and the path does not.
        Assert.Equal(ReferrerPolicies.Public, Header(response, SecurityHeaders.ReferrerPolicy));

        // Cook mode is the one feature a reader's page asks for, and it asks only for itself.
        Assert.Equal(PermissionsPolicies.Public, Header(response, SecurityHeaders.PermissionsPolicy));
        Assert.Contains("screen-wake-lock=(self)", Header(response, SecurityHeaders.PermissionsPolicy)!, StringComparison.Ordinal);
        Assert.Equal(ContentSecurityPolicies.Site, Header(response, SecurityHeaders.ContentSecurityPolicy));
    }

    [Fact]
    public async Task A_probe_answers_without_the_browser_headers()
    {
        // Both probes short-circuit the moment they are routed, ahead of every middleware the host adds. Nothing a
        // browser needs is missing, because no browser asks: Container Apps does, over the container's own port.
        await using var site = new SiteFactory("Development");

        using var response = await site.CreateReader().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(Header(response, SecurityHeaders.ContentTypeOptions));
    }

    [Fact]
    public async Task Hsts_is_claimed_outside_development_and_never_locally()
    {
        await using var local = new SiteFactory("Development");
        await using var deployed = new SiteFactory("Production");

        using var development = await local.CreateReader().GetAsync("/recipes/sourdough");
        using var production = await deployed.CreateReader().GetAsync("/anything");

        Assert.Null(Header(development, SecurityHeaders.StrictTransportSecurity));
        Assert.StartsWith("max-age=31536000", Header(production, SecurityHeaders.StrictTransportSecurity)!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reader_body_over_the_limit_is_refused()
    {
        // Everything a reader sends is small by rule - a rating, a comment, a signup, a contact message - so nothing
        // anonymous can make the public tier read more than that.
        await using var site = new SiteFactory("Development");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/ratings")
        {
            Content = Body(RequestLimits.PublicDefault + 1),
        };
        using var response = await site.CreateReader().SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);

        // The public tier has no problem vocabulary of its own yet (PP2, PP7), so the refusal carries the one the
        // shared middleware states: a stable code and words a reader could read, like every other surface.
        Assert.Equal(RequestLimits.TooLargeCode, await response.CodeAsync());
    }

    [Fact]
    public async Task A_reader_body_at_the_limit_is_not_refused()
    {
        await using var site = new SiteFactory("Development");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/ratings")
        {
            Content = Body(RequestLimits.PublicDefault),
        };
        using var response = await site.CreateReader().SendAsync(request);

        Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task A_reader_endpoints_answer_is_denied_everything()
    {
        // The branch every PP2 and PP7 endpoint will take: page data and reader writes are JSON, not documents.
        await using var site = new SiteFactory("Development");

        using var response = await site.CreateReader().GetAsync("/api/public/v1/pages/home");

        Assert.Equal(ContentSecurityPolicies.Api, Header(response, SecurityHeaders.ContentSecurityPolicy));
    }

    [Fact]
    public async Task Hsts_never_pins_a_bloggers_own_subdomains()
    {
        // DOM-001 allows a custom domain at its apex, and includeSubDomains from there would pin mail, shop, and
        // anything else the blogger runs - for a year, and not undone by leaving PantryPress.
        await using var site = new SiteFactory("Production");

        using var response = await site.CreateReader().GetAsync("/recipes/sourdough");

        Assert.Equal("max-age=31536000", Header(response, SecurityHeaders.StrictTransportSecurity));
    }

    [Theory]
    [InlineData("cook.localhost")]
    [InlineData("baker.localhost")]
    [InlineData("COOK.localhost")]
    [InlineData("cook.localhost.")]
    public async Task Every_tenant_host_is_answered_the_same_way(string host)
    {
        // Nothing here is per-site yet, and that is the point: the headers are process-wide constants, so no
        // tenant's configuration can reach another's response, and no host spelling is treated specially
        // (PSEC-004; the full two-site harness arrives with SiteHost at PP2).
        await using var site = new SiteFactory("Development");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/recipes/sourdough");
        request.Headers.Host = host;
        using var response = await site.CreateReader().SendAsync(request);

        Assert.Equal("nosniff", Header(response, SecurityHeaders.ContentTypeOptions));
        Assert.Equal(ContentSecurityPolicies.Site, Header(response, SecurityHeaders.ContentSecurityPolicy));
        Assert.Equal(ReferrerPolicies.Public, Header(response, SecurityHeaders.ReferrerPolicy));
    }

    [Fact]
    public async Task The_public_limit_is_far_below_the_admin_one()
    {
        // Stated as a fact about the two defaults, so a later change to either has to mean it.
        Assert.Equal(64 * 1024, RequestLimits.PublicDefault);
        Assert.True(RequestLimits.PublicDefault < RequestLimits.AdminDefault);
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
