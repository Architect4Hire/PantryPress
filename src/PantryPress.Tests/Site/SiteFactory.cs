using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PantryPress.Tests.Site;

/// <summary>
/// PantryPress.Site in memory. The public tier has no endpoints of its own yet (PP2, PP7); what it does have is the
/// edge every tenant browser meets first, which is what these tests are about.
/// </summary>
internal sealed class SiteFactory(string environment) : WebApplicationFactory<SiteEntryPoint>
{
    /// <summary>A tenant site host, as a reader's browser would address one (SITE-009).</summary>
    public static readonly Uri TenantOrigin = new("https://cook.localhost");

    /// <summary>A second tenant, for the host-header half of the two-site rule (tenancy.md).</summary>
    public static readonly Uri OtherTenantOrigin = new("https://baker.localhost");

    public HttpClient CreateReader(Uri? origin = null) =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = origin ?? TenantOrigin,
            AllowAutoRedirect = false,
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment(environment);

        // A deployed host refuses to start without a named proxy (PP1.8); a test host says so deliberately.
        builder.UseSetting("Edge:TrustedProxies:TrustNobody", "true");
    }
}
