using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Primitives;
using PantryPress.Domain.Modules.Sites.Contracts;
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
        // Both probes short-circuit the moment they are routed, ahead of every middleware the host adds - host
        // resolution included, since Container Apps addresses a container by whatever name it likes. Nothing a browser
        // needs is missing, because no browser asks. The liveness probe, because readiness asks the database, which this
        // edge-only host does not have (PP2.12).
        await using var site = new SiteFactory("Development");

        using var response = await site.CreateReader().GetAsync("/alive");

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
    [InlineData("nobody.localhost")]
    [InlineData("127.0.0.1")]
    public async Task Every_tenant_host_is_answered_the_same_way(string host)
    {
        // Nothing here is per-site yet, and that is the point: the headers are process-wide constants, so no
        // tenant's configuration can reach another's response, and no host spelling is treated specially - an unknown
        // host's refusal included (PSEC-004). Which site a host reaches is SiteHostTests', over the two-site harness.
        await using var site = new SiteFactory("Development");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/recipes/sourdough");
        request.Headers.Host = host;
        using var response = await site.CreateReader().SendAsync(request);

        Assert.Equal("nosniff", Header(response, SecurityHeaders.ContentTypeOptions));
        Assert.Equal(ContentSecurityPolicies.Site, Header(response, SecurityHeaders.ContentSecurityPolicy));
        Assert.Equal(ReferrerPolicies.Public, Header(response, SecurityHeaders.ReferrerPolicy));
    }

    [Theory]
    [InlineData("Edge:TrustedProxies:ForwardHost", "true")]
    [InlineData("Edge:TrustedProxies:AllowedHosts:0", "cook.localhost")]
    public async Task The_public_site_refuses_to_let_a_proxy_name_the_host(string setting, string value)
    {
        // PSEC-004 (PP2.12): here the Host is the tenant, so a proxy that may rewrite it may choose whose site a reader
        // sees. However a deployment copies the gateway's settings, the host will not start with that grant, with or
        // without a trusted peer named beside it.
        await using var site = new SiteFactory(
            "Production",
            new Dictionary<string, string?>
            {
                [setting] = value,
                ["Edge:TrustedProxies:KnownNetworks:0"] = "10.0.0.0/8",
            });

        var refused = await Assert.ThrowsAnyAsync<Exception>(() => Task.FromResult(site.Server));

        Assert.Contains("ForwardHost must stay off", refused.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task A_request_without_exactly_one_host_is_refused_without_a_lookup(int count)
    {
        // Kestrel refuses two Host headers itself; this holds the line behind it, so no host is guessed from a pair.
        var lookups = 0;
        await using var site = new SiteFactory(
            "Development",
            configureServices: services => services.Replace(
                ServiceDescriptor.Scoped<IPublicSiteResolution>(_ => new CountingResolution(() => lookups++))));
        var context = await site.Server.SendAsync(c =>
        {
            c.Request.Path = "/recipes/sourdough";
            c.Request.Headers.Host = count == 0 ? default : new StringValues([SiteFactory.TenantOrigin.Host, SiteFactory.OtherTenantOrigin.Host]);
        });

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(0, lookups);
    }

    private sealed class CountingResolution(Action counted) : IPublicSiteResolution
    {
        public Task<PublicSite?> ResolveAsync(string? hostHeader, CancellationToken cancellationToken)
        {
            counted();
            return Task.FromResult<PublicSite?>(null);
        }
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
