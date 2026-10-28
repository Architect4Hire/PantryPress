using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Tests.Api;

namespace PantryPress.Tests.Site;

/// <summary>
/// PantryPress.Site in memory. With no settings it is the edge alone: host resolution is a fixed map of the two
/// <see cref="TenantOrigin"/> hosts, and nothing reaches a database or a cache. With the migrated database's connection
/// strings (<see cref="SiteHostTests"/>, the two-site harness) it is the real host, resolving through the host map.
/// </summary>
internal sealed class SiteFactory(
    string environment,
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<SiteEntryPoint>
{
    /// <summary>A tenant site host, as a reader's browser would address one (SITE-009).</summary>
    public static readonly Uri TenantOrigin = new("https://cook.localhost");

    /// <summary>A second tenant, for the host-header half of the two-site rule (tenancy.md).</summary>
    public static readonly Uri OtherTenantOrigin = new("https://baker.localhost");

    /// <summary>Nothing listens here; a host that never opens a connection still needs a well-formed connection string.</summary>
    public const string UnreachableCache = "127.0.0.1:1,abortConnect=false,connectTimeout=200,connectRetry=0";

    /// <summary>The real host over a database and a cache, with probe routes and any extra services.</summary>
    public static SiteFactory Over(
        string databaseConnection,
        string cacheConnection,
        Action<IServiceCollection>? configureServices = null) =>
        new(
            "Development",
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:pantrypress"] = databaseConnection,
                ["ConnectionStrings:redis"] = cacheConnection,
            },
            services =>
            {
                services.AddTransient<IStartupFilter, SiteProbes>();
                configureServices?.Invoke(services);
            });

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
        builder.UseSetting("ConnectionStrings:pantrypress", ApiServiceFactory.UnreachableDatabase);
        builder.UseSetting("ConnectionStrings:redis", UnreachableCache);
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            if (settings is null)
            {
                services.Replace(ServiceDescriptor.Scoped<IPublicSiteResolution, FixedHosts>());
            }

            configureServices?.Invoke(services);
        });
    }

    // The edge tests' two tenants, without a database: the real Host reading, then a fixed answer, entered as a resolver
    // enters it.
    private sealed class FixedHosts(ISiteContextWriter siteContext) : IPublicSiteResolution
    {
        private static readonly Dictionary<string, SiteId> Sites = new(StringComparer.Ordinal)
        {
            [TenantOrigin.Host] = new SiteId(Guid.Parse("0f3c8a52-5f1e-4c49-9c0e-2b1f6c0b7a01")),
            [OtherTenantOrigin.Host] = new SiteId(Guid.Parse("0f3c8a52-5f1e-4c49-9c0e-2b1f6c0b7a02")),
        };

        public Task<PublicSite?> ResolveAsync(string? hostHeader, CancellationToken cancellationToken)
        {
            if (!PublicHostNames.TryRead(hostHeader, out var host) || !Sites.TryGetValue(host, out var site))
            {
                return Task.FromResult<PublicSite?>(null);
            }

            siteContext.Enter(site);
            return Task.FromResult<PublicSite?>(new PublicSite(site, host, host, true, SiteStatus.Live));
        }
    }
}
