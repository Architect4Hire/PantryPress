using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.MigrationService;
using PantryPress.Tests.Api;
using PantryPress.Tests.Persistence;
using PantryPress.Tests.Site;
using StackExchange.Redis;

namespace PantryPress.Tests.TwoSite;

// Inside the namespace, so the types win over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;

/// <summary>One of the two look-alike sites, its canonical subdomain, and the members it was seeded with.</summary>
internal sealed record SeededSite(
    SiteId Id,
    string Slug,
    string Host,
    AccountId Owner,
    AccountId Editor,
    AccountId Author,
    AccountId Suspended);

/// <summary>
/// Two sites that look alike (tenancy.md, "Two-site test"; PP2.7): the same name and tagline, the same members by role,
/// and slugs one letter apart, A's a prefix of B's, so a match on a prefix or a near miss shows; each answers on its
/// subdomain, canonical (PP2.9), so the hosts are as alike as the slugs. Three accounts cross the
/// sites: <see cref="Shared"/> is an Author on A and an Owner on B, carrying a different role into each;
/// <see cref="SuspendedOnA"/> is suspended on A and an Editor on B, carrying a different standing into each; and
/// <see cref="Stranger"/> belongs to neither.
/// </summary>
internal sealed record TwoSiteWorld(SeededSite A, SeededSite B, AccountId Shared, AccountId SuspendedOnA, AccountId Stranger);

/// <summary>
/// The two-site harness (PP2.7; ARCH-015, PSEC-007): the services a host registers over the migrated database, two
/// look-alike sites seeded per test with fresh ids so tests share the database safely, scopes that work on one site or
/// as one account, and - when asked for - ApiService signed in as any account. The checks a change owes live in
/// <see cref="TwoSiteChecks"/>.
/// </summary>
internal sealed class TwoSiteHarness : IAsyncDisposable
{
    private static readonly DateTimeOffset SeededAt = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The platform's zone the seeded subdomains sit under, as in production.</summary>
    public const string SubdomainZone = ApiServiceFactory.SubdomainZone;

    private readonly ServiceProvider _services;
    private readonly ApiServiceFactory? _api;
    private readonly SiteFactory? _site;

    private TwoSiteHarness(ServiceProvider services, ApiServiceFactory? api, SiteFactory? site, string cacheConnection)
    {
        _services = services;
        _api = api;
        _site = site;
        CacheConnection = cacheConnection;
    }

    /// <summary>The test AppHost's Redis, which the harness, ApiService, and the public site share.</summary>
    public string CacheConnection { get; }

    /// <summary>The public site's own services, for what only its host registers.</summary>
    public IServiceProvider SiteServices =>
        (_site ?? throw new InvalidOperationException("This harness was made without the public site.")).Services;

    public IServiceProvider Services => _services;

    /// <summary>ApiService's own services, for what only its host registers: its key ring, its outbox handlers.</summary>
    public IServiceProvider ApiServices =>
        (_api ?? throw new InvalidOperationException("This harness was made without ApiService.")).Services;

    /// <param name="database">The shared migrated database.</param>
    /// <param name="withApi">Also start ApiService over that database, with the site policy probe routes.</param>
    /// <param name="configure">More context configuration, such as a planted test model.</param>
    /// <param name="apiSettings">ApiService settings over the harness's own, such as a site limit under test.</param>
    /// <param name="apiServices">More ApiService services, such as the Worker's half of an outbox message under test.</param>
    /// <param name="withSite">
    /// Also start the public site over that database and the test AppHost's Redis, with its probe route (PP2.12).
    /// </param>
    /// <param name="siteServices">More public-site services, such as a counter of the queries it makes.</param>
    /// <param name="domainServices">More services for the harness's own scopes, after its own, such as a fixed clock (PP4.4).</param>
    public static async Task<TwoSiteHarness> CreateAsync(
        MigratedDatabase database,
        bool withApi = false,
        Action<DbContextOptionsBuilder>? configure = null,
        IReadOnlyDictionary<string, string?>? apiSettings = null,
        Action<IServiceCollection>? apiServices = null,
        bool withSite = false,
        Action<IServiceCollection>? siteServices = null,
        Action<IServiceCollection>? domainServices = null)
    {
        var connectionString = await database.App.GetConnectionStringAsync(MigrationServiceExtensions.ConnectionName, CancellationToken.None)
            ?? throw new InvalidOperationException("The test AppHost has no pantrypress connection string.");
        var cacheConnection = await database.App.GetConnectionStringAsync("redis", CancellationToken.None)
            ?? throw new InvalidOperationException("The test AppHost has no redis connection string.");

        ApiServiceFactory? api = null;
        if (withApi)
        {
            // The seeded accounts have no confirmed address, and most tests create more than one site with them: the
            // limit on unconfirmed accounts is lifted here and tested with its own setting.
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:pantrypress"] = connectionString,
                ["ConnectionStrings:redis"] = cacheConnection,
                ["Gateway:SharedSecret"] = TestSecrets.GatewaySecret,
                ["RateLimits:SignedInAccountPerMinute"] = "100000",
                ["RateLimits:SitesPerAccountPerMinute"] = "100000",
                ["RateLimits:SlugChecksPerAccountPerMinute"] = "100000",
                ["RateLimits:SiteCreatesPerAccountPerHour"] = "100000",
                ["RateLimits:InvitesPerAccountPerHour"] = "100000",
                ["RateLimits:InvitationAcceptsPerAccountPerHour"] = "100000",
                ["RateLimits:SiteMembersPerAccountPerMinute"] = "100000",
                ["RateLimits:SiteLookupsPerAccountPerMinute"] = "100000",
                ["RateLimits:SiteMemberChangesPerAccountPerMinute"] = "100000",
                ["Sites:UnconfirmedAccountSiteLimit"] = "100000",
            };
            foreach (var (key, value) in apiSettings ?? new Dictionary<string, string?>())
            {
                settings[key] = value;
            }

            api = new ApiServiceFactory(
                "Testing",
                settings,
                services =>
                {
                    services.AddProbes(new IdempotencyProbeLedger(), typeof(SitePolicyProbeController));
                    apiServices?.Invoke(services);
                });
            _ = api.Server;
        }

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sites:SubdomainZone"] = SubdomainZone })
            .Build());
        services.AddApplicationClock();
        services.AddLogging();
        services.AddAccountsIdentitySchema();
        services.AddPantryPressDbContext((_, options) =>
        {
            options.UseSqlServer(connectionString, MigrationServiceExtensions.UseMigrationsAssembly);
            configure?.Invoke(options);
        });
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(cacheConnection));
        services.AddSites();
        services.AddRecipes();
        domainServices?.Invoke(services);

        SiteFactory? site = null;
        if (withSite)
        {
            site = SiteFactory.Over(connectionString, cacheConnection, siteServices);
            _ = site.Server;
        }

        return new TwoSiteHarness(services.BuildServiceProvider(validateScopes: true), api, site, cacheConnection);
    }

    public static AccountId NewAccount() => new(Guid.NewGuid());

    /// <summary>
    /// Seeds two look-alike sites and their members, with ids and slugs no other test shares. The
    /// <paramref name="prefix"/> is a slug's start - lowercase letters, digits, and hyphens, at most 20 - so enough of a
    /// fresh id follows it to keep every test's slugs apart.
    /// </summary>
    public async Task<TwoSiteWorld> SeedAsync(string prefix = "supper")
    {
        // A hyphen joins the prefix to the id, so it may not end with one or hold two together: the slug would break the
        // slug policy's rules, which the schema enforces too (PP2.8).
        if (prefix.Length is 0 or > 20
            || !prefix.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
            || !char.IsAsciiLetterLower(prefix[0])
            || prefix[^1] == '-'
            || prefix.Contains("--", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A seed prefix is 1 to 20 lowercase letters, digits, or single hyphens, starting with a letter and not ending with a hyphen.",
                nameof(prefix));
        }

        // As alike as two slugs can be: B's is A's with one more letter.
        var stem = $"{prefix}-{Guid.NewGuid():N}";
        stem = stem[..Math.Min(stem.Length, SiteLimits.MaxSlugLength - 1)];
        var (a, aMembers) = NewSite(stem);
        var (b, bMembers) = NewSite(stem + "a");
        var world = new TwoSiteWorld(a, b, NewAccount(), NewAccount(), NewAccount());

        await ChangeAsync(db =>
        {
            db.AddRange(SiteRow(a), SiteRow(b));
            db.AddRange(SubdomainRow(a), SubdomainRow(b));
            db.AddRange(aMembers.Concat(bMembers));
            db.Add(Member(a.Id, world.Shared, SiteRole.Author));
            db.Add(Member(b.Id, world.Shared, SiteRole.Owner));
            db.Add(Member(a.Id, world.SuspendedOnA, SiteRole.Editor, SiteMemberStatus.Suspended));
            db.Add(Member(b.Id, world.SuspendedOnA, SiteRole.Editor));
        });
        return world;
    }

    /// <summary>A scope working on no site, as an account route or a refused request does.</summary>
    public AsyncServiceScope Scope() => _services.CreateAsyncScope();

    /// <summary>A scope working on <paramref name="site"/>, as a resolver would have entered it.</summary>
    public AsyncServiceScope In(SiteId site) => TwoSiteChecks.In(_services, site);

    /// <summary>ApiService, signed in as <paramref name="account"/>.</summary>
    public HttpClient ClientFor(AccountId account) =>
        (_api ?? throw new InvalidOperationException("This harness was made without ApiService.")).CreateClientFor(account.Value);

    /// <summary>
    /// What the public site resolves <paramref name="host"/> to, through its whole pipeline (PP2.12): the probe's answer,
    /// or the refusal. The Host header is sent exactly as given.
    /// </summary>
    public async Task<HttpResponseMessage> ReadAsAsync(string host, string path = SiteProbes.Path, Action<HttpRequestMessage>? configure = null)
    {
        var client = (_site ?? throw new InvalidOperationException("This harness was made without the public site.")).CreateReader();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = host;
        configure?.Invoke(request);
        return await client.SendAsync(request);
    }

    /// <summary>Changes rows directly, past every facade, as a test's setup does: the tenant root, working on no site.</summary>
    public async Task ChangeAsync(Action<PantryPressDbContext> change)
    {
        await using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        change(db);
        await db.SaveChangesAsync();
    }

    /// <summary>Changes rows directly, working on <paramref name="site"/>: site-owned rows are read and stamped as its.</summary>
    public async Task ChangeAsync(SiteId site, Action<PantryPressDbContext> change)
    {
        await using var scope = In(site);
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        change(db);
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        if (_site is not null)
        {
            await _site.DisposeAsync();
        }

        await _services.DisposeAsync();
    }

    private static (SeededSite Site, SiteMember[] Members) NewSite(string slug)
    {
        var site = new SeededSite(
            new SiteId(Guid.NewGuid()),
            slug,
            $"{slug}.{SubdomainZone}",
            NewAccount(),
            NewAccount(),
            NewAccount(),
            NewAccount());
        SiteMember[] members =
        [
            Member(site.Id, site.Owner, SiteRole.Owner),
            Member(site.Id, site.Editor, SiteRole.Editor),
            Member(site.Id, site.Author, SiteRole.Author),
            Member(site.Id, site.Suspended, SiteRole.Editor, SiteMemberStatus.Suspended),
        ];
        return (site, members);
    }

    private static Site SiteRow(SeededSite site) => new()
    {
        Id = site.Id,
        Slug = site.Slug,
        Name = "Sunday Supper",
        Tagline = "Weeknight dinners",
        Status = SiteStatus.Live,
        TimeZone = "America/Chicago",
        Locale = "en-US",
        CreatedAt = SeededAt,
    };

    private static SiteHost SubdomainRow(SeededSite site) => new()
    {
        Id = new SiteHostId(Guid.NewGuid()),
        SiteId = site.Id,
        Host = site.Host,
        Kind = SiteHostKind.Subdomain,
        IsCanonical = true,
        Status = SiteHostStatus.Active,
        CreatedAt = SeededAt,
    };

    private static SiteMember Member(SiteId site, AccountId account, SiteRole role, SiteMemberStatus status = SiteMemberStatus.Active) => new()
    {
        SiteId = site,
        AccountId = account,
        Role = role,
        Status = status,
        CreatedAt = SeededAt,
    };
}
