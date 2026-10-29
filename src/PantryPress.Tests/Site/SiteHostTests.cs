using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PantryPress.Domain.Managers.Caching;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Managers.Time;
using Microsoft.Extensions.Logging.Abstractions;
using PantryPress.Domain.Modules.Sites.Repositories;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.DataLayer;
using PantryPress.Tests.Persistence;
using PantryPress.Tests.TwoSite;
using StackExchange.Redis;

namespace PantryPress.Tests.Site;

// Inside the namespace, so the types win over this namespace's own name.
using PantryPress.Domain.Modules.Sites;

// PP2.12 (ARCH-003, SITE-005, SITE-009, PSEC-004): the public site resolves a request's site from its Host header alone,
// through the host map, and over the two-site harness: known, unknown, mixed-case, port, and www hosts; the host-header
// tricks; no SQL once the map is warm; and a host that changes hands, a canonical host that moves, a status that changes,
// or a site that is created resolves its new answer at once, never another site's.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class SiteHostTests(MigratedDatabase database) : IAsyncLifetime
{
    private static readonly DateTimeOffset AddedAt = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    // One counter for the class, as in SiteContextTests: a new interceptor per test builds EF Core a new service provider.
    private static readonly CommandCounter Queries = new();
    private readonly CacheCounter _cacheCalls = new();
    private TwoSiteHarness _harness = null!;
    private TwoSiteWorld _world = null!;
    private ConnectionMultiplexer _redis = null!;

    public async Task InitializeAsync()
    {
        _harness = await TwoSiteHarness.CreateAsync(
            database,
            withApi: true,
            withSite: true,
            siteServices: services =>
            {
                services.ConfigureDbContext<PantryPressDbContext>(options => options.AddInterceptors(Queries));
                services.Replace(ServiceDescriptor.Singleton<IPlatformCache>(sp =>
                    new CountingCache(new RedisPlatformCache(sp.GetRequiredService<IConnectionMultiplexer>()), _cacheCalls)));
            });
        _world = await _harness.SeedAsync("hosts");
        _redis = await ConnectionMultiplexer.ConnectAsync(_harness.CacheConnection);
    }

    public async Task DisposeAsync()
    {
        await _redis.DisposeAsync();
        await _harness.DisposeAsync();
    }

    [Fact]
    public async Task Each_sites_host_resolves_that_site_and_enters_it()
    {
        var a = await ProbeAsync(_world.A.Host);
        var b = await ProbeAsync(_world.B.Host);

        Assert.Equal(new SiteProbeAnswer(_world.A.Id.Value, _world.A.Host, _world.A.Host, true, "Live", _world.A.Id.Value), a);
        Assert.Equal(new SiteProbeAnswer(_world.B.Id.Value, _world.B.Host, _world.B.Host, true, "Live", _world.B.Id.Value), b);
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("{0}.")]
    [InlineData("{0}:443")]
    [InlineData("{0}:7053")]
    [InlineData("{0}.:8443")]
    public async Task Case_a_trailing_dot_and_a_port_name_the_same_host(string spelling)
    {
        foreach (var host in new[] { _world.A.Host, _world.A.Host.ToUpperInvariant(), MixedCase(_world.A.Host) })
        {
            var answer = await ProbeAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, spelling, host));
            Assert.Equal(_world.A.Id.Value, answer.SiteId);
            Assert.Equal(_world.A.Host, answer.Host);
        }
    }

    [Fact]
    public async Task An_unknown_host_is_answered_exactly_as_an_unknown_path_on_a_real_site()
    {
        using var unknownHost = await _harness.ReadAsAsync($"nobody-{Guid.NewGuid():N}.{TwoSiteHarness.SubdomainZone}");
        using var unknownPath = await _harness.ReadAsAsync(_world.A.Host, "/api/public/v1/nothing-here");

        await TwoSiteChecks.AssertIndistinguishableAsync(unknownHost, unknownPath, HttpStatusCode.NotFound, "not-found");
        Assert.Equal("no-store", unknownHost.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("application/xml")]
    [InlineData("text/plain")]
    public async Task A_refusal_is_the_problem_document_whatever_the_reader_accepts(string accept)
    {
        using var unknownHost = await _harness.ReadAsAsync(
            $"nobody-{Guid.NewGuid():N}.{TwoSiteHarness.SubdomainZone}",
            configure: r => r.Headers.Accept.ParseAdd(accept));
        using var unknownPath = await _harness.ReadAsAsync(
            _world.A.Host,
            "/api/public/v1/nothing-here",
            r => r.Headers.Accept.ParseAdd(accept));

        await TwoSiteChecks.AssertIndistinguishableAsync(unknownHost, unknownPath, HttpStatusCode.NotFound, "not-found");
        Assert.Equal("no-store", unknownHost.Headers.CacheControl?.ToString());
        Assert.Equal("no-store", unknownPath.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task A_site_at_its_local_address_answers_there_with_any_port()
    {
        // SITE-009: a local run's zone is localhost, so a new site answers at {slug}.localhost on the Site's own port.
        await using var local = await TwoSiteHarness.CreateAsync(
            database,
            withApi: true,
            apiSettings: new Dictionary<string, string?> { ["Sites:SubdomainZone"] = "localhost" },
            withSite: true);
        var slug = $"local-{Guid.NewGuid():N}"[..26];

        using var created = await local.ClientFor(_world.Stranger).PostAsJsonAsync(
            "/api/v1/sites",
            new CreateSiteRequest(slug, "Local Kitchen", null, "America/Chicago", null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        foreach (var host in new[] { $"{slug}.localhost", $"{slug}.localhost:7053", $"{slug.ToUpperInvariant()}.LOCALHOST.:5095" })
        {
            using var response = await local.ReadAsAsync(host);
            var answer = await response.Content.ReadFromJsonAsync<SiteProbeAnswer>();
            Assert.Equal($"{slug}.localhost", answer!.Host);
            Assert.True(answer.IsCanonical);
            Assert.Equal(answer.SiteId, answer.ContextSite);
        }

        // PPL.1 (PP-D19): in a local run the platform home is the bare localhost, and app. and www. under it are hosts
        // the gateway refuses; none of them is a site, whatever port it is asked on.
        foreach (var host in new[] { "localhost", "localhost:7044", "LOCALHOST.", "app.localhost", "www.localhost", $"www.{slug}.localhost" })
        {
            using var response = await local.ReadAsAsync(host);
            await AssertNotFoundAsync(response, host);
        }
    }

    [Fact]
    public async Task A_lookup_slower_than_the_fill_delay_answers_but_caches_nothing()
    {
        var host = $"slow-{Guid.NewGuid():N}.example";
        await AddHostAsync(_world.A.Id, host, SiteHostKind.Custom);
        await using var scope = _harness.Scope();
        var map = new SiteHostMapDataLayer(
            scope.ServiceProvider.GetRequiredService<SiteHostRepository>(),
            new RedisPlatformCache(_redis),
            new SteppingClock(SiteHostMap.MaxFillDelay + TimeSpan.FromSeconds(1)),
            NullLogger<SiteHostMapDataLayer>.Instance);

        var found = await map.FindAsync(host, CancellationToken.None);

        Assert.Equal(_world.A.Id, found?.SiteId);
        Assert.False(await _redis.GetDatabase().KeyExistsAsync(PlatformCacheKey.For(SiteHostMap.Area, host).Value));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:443")]
    [InlineData("10.0.0.4")]
    [InlineData("127.1")]
    [InlineData("2130706433")]
    [InlineData("127.0.0.0x1")]
    [InlineData("[::1]")]
    [InlineData("[::1]:443")]
    [InlineData("localhost")]
    public async Task An_ip_literal_or_bare_name_is_refused_before_the_cache_or_the_database(string host)
    {
        await ProbeAsync(_world.A.Host);
        Queries.Reset();
        _cacheCalls.Reset();

        using var response = await _harness.ReadAsAsync(host);

        await AssertNotFoundAsync(response);
        Assert.Equal(0, Queries.Count);
        Assert.Equal(0, _cacheCalls.Count);
    }

    [Fact]
    public async Task A_warm_host_is_answered_with_no_sql()
    {
        var unknown = $"nobody-{Guid.NewGuid():N}.{TwoSiteHarness.SubdomainZone}";
        await ProbeAsync(_world.A.Host);
        using (await _harness.ReadAsAsync(unknown))
        {
        }

        Queries.Reset();
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(_world.A.Id.Value, (await ProbeAsync(MixedCase(_world.A.Host) + ":443")).SiteId);
            using var refused = await _harness.ReadAsAsync(unknown);
            await AssertNotFoundAsync(refused);
        }

        Assert.Equal(0, Queries.Count);
    }

    [Fact]
    public async Task A_cold_host_is_read_once_and_then_from_the_map()
    {
        Queries.Reset();
        await ProbeAsync(_world.B.Host);
        var cold = Queries.Count;
        await ProbeAsync(_world.B.Host);

        Assert.Equal(1, cold);
        Assert.Equal(1, Queries.Count);
    }

    [Fact]
    public async Task A_forwarded_host_never_chooses_the_site()
    {
        foreach (var (name, value) in TwoSiteChecks.ForwardingTricks(_world.B.Host))
        {
            using var response = await _harness.ReadAsAsync(_world.A.Host, configure: r => r.Headers.TryAddWithoutValidation(name, value));
            var answer = await response.Content.ReadFromJsonAsync<SiteProbeAnswer>();
            Assert.Equal(_world.A.Id.Value, answer!.SiteId);
        }

        // And a host the forwarding header names, sent to no real host, still finds nothing.
        using var nowhere = await _harness.ReadAsAsync(
            $"nobody-{Guid.NewGuid():N}.{TwoSiteHarness.SubdomainZone}",
            configure: r => r.Headers.TryAddWithoutValidation("X-Forwarded-Host", _world.B.Host));
        await AssertNotFoundAsync(nowhere);
    }

    [Fact]
    public async Task A_host_near_another_sites_never_reaches_it()
    {
        var a = _world.A.Host;
        var b = _world.B.Host;
        foreach (var host in TwoSiteChecks.HostTricks(_world))
        {
            using var response = await _harness.ReadAsAsync(host);
            await AssertNotFoundAsync(response, host);
        }

        // The near misses changed nothing: each site still resolves itself.
        Assert.Equal(_world.A.Id.Value, (await ProbeAsync(a)).SiteId);
        Assert.Equal(_world.B.Id.Value, (await ProbeAsync(b)).SiteId);
    }

    [Fact]
    public async Task A_custom_domain_its_www_variant_and_the_old_subdomain_resolve_their_site_with_its_canonical_host()
    {
        var custom = $"bake-{Guid.NewGuid():N}.example";
        await AddHostAsync(_world.B.Id, custom, SiteHostKind.Custom);
        await AddHostAsync(_world.B.Id, $"www.{custom}", SiteHostKind.WwwVariant);
        await MoveCanonicalAsync(_world.B.Id, custom);

        Assert.Equal(new SiteProbeAnswer(_world.B.Id.Value, custom, custom, true, "Live", _world.B.Id.Value), await ProbeAsync(custom));
        Assert.Equal(
            new SiteProbeAnswer(_world.B.Id.Value, $"www.{custom}", custom, false, "Live", _world.B.Id.Value),
            await ProbeAsync($"WWW.{custom.ToUpperInvariant()}:443"));
        Assert.Equal(
            new SiteProbeAnswer(_world.B.Id.Value, _world.B.Host, custom, false, "Live", _world.B.Id.Value),
            await ProbeAsync(_world.B.Host));

        // B's domain never names A, under any spelling, and A's own answer is untouched.
        Assert.NotEqual(_world.A.Id.Value, (await ProbeAsync($"{custom}.")).SiteId);
        Assert.Equal(new SiteProbeAnswer(_world.A.Id.Value, _world.A.Host, _world.A.Host, true, "Live", _world.A.Id.Value), await ProbeAsync(_world.A.Host));
    }

    [Fact]
    public async Task A_pending_host_is_answered_exactly_like_an_unknown_one()
    {
        var pending = $"pending-{Guid.NewGuid():N}.example";
        await AddHostAsync(_world.A.Id, pending, SiteHostKind.Custom, SiteHostStatus.Pending);

        using var held = await _harness.ReadAsAsync(pending);
        using var unknown = await _harness.ReadAsAsync($"unknown-{Guid.NewGuid():N}.example");

        await TwoSiteChecks.AssertIndistinguishableAsync(held, unknown, HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task A_host_that_changes_hands_resolves_to_its_new_site_at_once()
    {
        var shared = $"moving-{Guid.NewGuid():N}.example";
        await AddHostAsync(_world.A.Id, shared, SiteHostKind.Custom);
        Assert.Equal(_world.A.Id.Value, (await ProbeAsync(shared)).SiteId);
        var bEntry = await EntryAsync(_world.B.Host);

        // Removed from A and added to B, as a host changes hands: two writes, each refreshing the map.
        await _harness.ChangeAsync(db => db.Set<SiteHost>().Where(h => h.Host == shared).ExecuteDelete());
        await RefreshAsync(shared);
        using (var between = await _harness.ReadAsAsync(shared))
        {
            await AssertNotFoundAsync(between);
        }

        await AddHostAsync(_world.B.Id, shared, SiteHostKind.Custom);
        await RefreshAsync(shared);

        Assert.Equal(_world.B.Id.Value, (await ProbeAsync(shared)).SiteId);
        Assert.Equal(bEntry, await EntryAsync(_world.B.Host));
    }

    [Fact]
    public async Task Without_its_refresh_a_write_is_not_seen_which_is_why_every_write_owes_one()
    {
        var host = $"stale-{Guid.NewGuid():N}.example";
        await AddHostAsync(_world.A.Id, host, SiteHostKind.Custom);
        Assert.Equal(_world.A.Id.Value, (await ProbeAsync(host)).SiteId);

        await _harness.ChangeAsync(db => db.Set<SiteHost>().Where(h => h.Host == host).ExecuteDelete());

        Assert.Equal(_world.A.Id.Value, (await ProbeAsync(host)).SiteId);
        await RefreshAsync(host);
        using var refreshed = await _harness.ReadAsAsync(host);
        await AssertNotFoundAsync(refreshed);
    }

    [Fact]
    public async Task A_canonical_host_that_moves_is_seen_at_once_and_the_other_site_is_untouched()
    {
        var custom = $"cook-{Guid.NewGuid():N}.example";
        await AddHostAsync(_world.A.Id, custom, SiteHostKind.Custom);
        Assert.True((await ProbeAsync(_world.A.Host)).IsCanonical);
        await ProbeAsync(_world.B.Host);
        var bEntry = await EntryAsync(_world.B.Host);

        await MoveCanonicalAsync(_world.A.Id, custom);
        await RefreshSiteAsync(_world.A.Id);

        Assert.Equal(
            new SiteProbeAnswer(_world.A.Id.Value, _world.A.Host, custom, false, "Live", _world.A.Id.Value),
            await ProbeAsync(_world.A.Host));
        Assert.True((await ProbeAsync(custom)).IsCanonical);
        Assert.Equal(bEntry, await EntryAsync(_world.B.Host));
        Assert.True((await ProbeAsync(_world.B.Host)).IsCanonical);
    }

    [Fact]
    public async Task A_change_of_status_is_seen_at_once_and_the_other_site_keeps_its_own()
    {
        await ProbeAsync(_world.A.Host);
        await ProbeAsync(_world.B.Host);
        var bEntry = await EntryAsync(_world.B.Host);

        await _harness.ChangeAsync(db => db.Set<Domain.Modules.Sites.Site>()
            .Where(s => s.Id == _world.A.Id)
            .ExecuteUpdate(set => set.SetProperty(s => s.Status, SiteStatus.Suspended)));
        await RefreshSiteAsync(_world.A.Id);

        Assert.Equal("Suspended", (await ProbeAsync(_world.A.Host)).Status);
        Assert.Equal("Live", (await ProbeAsync(_world.B.Host)).Status);
        Assert.Equal(bEntry, await EntryAsync(_world.B.Host));
    }

    [Fact]
    public async Task A_lookup_that_raced_a_write_never_caches_the_old_answer()
    {
        var host = $"race-{Guid.NewGuid():N}.example";
        await AddHostAsync(_world.A.Id, host, SiteHostKind.Custom);
        var key = PlatformCacheKey.For(SiteHostMap.Area, host);
        var cache = new RedisPlatformCache(_redis);

        // The lookup reads the map and the database before the host changes hands...
        var seen = await cache.ReadAsync(key);
        var old = SiteHostMap.Write(new SiteHostMatch(
            _world.A.Id,
            SiteStatus.Live,
            false,
            _world.A.Host,
            new PublicSiteIdentity("Sunday Supper", "Weeknight dinners", "en-US")));

        // ...the host moves to B and the map is refreshed...
        await _harness.ChangeAsync(db => db.Set<SiteHost>().Where(h => h.Host == host).ExecuteDelete());
        await AddHostAsync(_world.B.Id, host, SiteHostKind.Custom);
        await RefreshAsync(host);

        // ...and the lookup's fill, arriving last, is refused, so B's readers never land on A.
        Assert.False(await cache.WriteAsync(key, seen, old, SiteHostMap.FoundLifetime));
        Assert.Equal(_world.B.Id.Value, (await ProbeAsync(host)).SiteId);
    }

    [Fact]
    public async Task A_site_created_after_its_address_was_asked_for_answers_at_once()
    {
        var slug = $"brand-new-{Guid.NewGuid():N}"[..30];
        var host = $"{slug}.{TwoSiteHarness.SubdomainZone}";
        using (var before = await _harness.ReadAsAsync(host))
        {
            await AssertNotFoundAsync(before);
        }

        Assert.Equal("-", (await _redis.GetDatabase().HashGetAsync(PlatformCacheKey.For(SiteHostMap.Area, host).Value, "v")).ToString());

        using var created = await _harness.ClientFor(_world.Stranger).PostAsJsonAsync(
            "/api/v1/sites",
            new CreateSiteRequest(slug, "Brand New", null, "America/Chicago", null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var answer = await ProbeAsync(host);
        Assert.Equal(host, answer.Host);
        Assert.True(answer.IsCanonical);
        Assert.NotEqual(_world.A.Id.Value, answer.SiteId);
        Assert.NotEqual(_world.B.Id.Value, answer.SiteId);
    }

    [Fact]
    public async Task Every_entry_the_map_writes_is_a_platform_key_under_its_own_area()
    {
        await ProbeAsync(_world.A.Host);
        await ProbeAsync(MixedCase(_world.B.Host) + ".");
        using (await _harness.ReadAsAsync($"{_world.A.Slug}-gone.{TwoSiteHarness.SubdomainZone}"))
        {
        }

        var server = _redis.GetServers().Single();
        var keys = server.Keys(pattern: $"*{_world.A.Slug}*").Select(k => k.ToString()).ToList();

        Assert.Equal(3, keys.Count);
        Assert.All(keys, k => Assert.StartsWith($"{PlatformCacheKey.Namespace}:{SiteHostMap.Area}:", k, StringComparison.Ordinal));
        Assert.Contains(PlatformCacheKey.For(SiteHostMap.Area, _world.A.Host).Value, keys);
        Assert.Contains(PlatformCacheKey.For(SiteHostMap.Area, _world.B.Host).Value, keys);
    }

    [Fact]
    public async Task Nothing_on_this_host_signs_anyone_in_or_sets_a_cookie()
    {
        string[] paths = [SiteProbes.Path, "/api/public/v1/site", "/api/public/v1/nothing-here", "/", "/health", "/alive"];
        foreach (var path in paths)
        {
            using var known = await _harness.ReadAsAsync(_world.A.Host, path);
            using var unknown = await _harness.ReadAsAsync($"nobody-{Guid.NewGuid():N}.example", path);
            Assert.False(known.Headers.Contains("Set-Cookie"), path);
            Assert.False(unknown.Headers.Contains("Set-Cookie"), path);
        }

        var services = _harness.SiteServices;
        Assert.Null(services.GetService<IAuthenticationSchemeProvider>());
        Assert.Null(services.GetService<IAuthenticationService>());
        Assert.Null(services.GetService<IAntiforgery>());
        Assert.Null(services.GetService<ISessionStore>());
        Assert.Null(services.GetService<IUserStore<ApplicationUser>>());
        await using var scope = services.CreateAsyncScope();
        Assert.Null(scope.ServiceProvider.GetService<UserManager<ApplicationUser>>());
        Assert.Null(scope.ServiceProvider.GetService<SignInManager<ApplicationUser>>());
    }

    // PPL.1 (PP-D19): the platform home is the bare localhost, and a browser does not keep localhost's cookies apart by
    // port, so the gateway's session and antiforgery cookies reach the Site's port too. They must buy nothing here: the
    // apex is still the neutral 404, a site still resolves by its Host alone, and nothing is set in answer.
    [Fact]
    public async Task The_platform_homes_cookies_change_nothing_here()
    {
        var cookies = string.Join("; ", new[]
        {
            PantryPress.Gateway.Hosting.SessionCookie.CompleteName,
            PantryPress.Gateway.Hosting.SessionCookie.SecondFactorName,
            PantryPress.Gateway.Hosting.GatewayAntiforgery.CookieName,
            PantryPress.Gateway.Hosting.GatewayAntiforgery.TokenCookieName,
        }.Select(name => $"{name}=forged"));
        void WithCookies(HttpRequestMessage r) => r.Headers.TryAddWithoutValidation("Cookie", cookies);

        foreach (var apex in new[] { "localhost", "localhost:7044", "localhost:7053" })
        {
            using var bare = await _harness.ReadAsAsync(apex);
            using var carrying = await _harness.ReadAsAsync(apex, configure: WithCookies);
            await TwoSiteChecks.AssertIndistinguishableAsync(bare, carrying, HttpStatusCode.NotFound, "not-found");
            Assert.False(carrying.Headers.Contains("Set-Cookie"), apex);
        }

        using var site = await _harness.ReadAsAsync(_world.A.Host, configure: WithCookies);
        Assert.Equal(_world.A.Id.Value, (await site.Content.ReadFromJsonAsync<SiteProbeAnswer>())!.SiteId);
        Assert.False(site.Headers.Contains("Set-Cookie"));

        // The site context is cached publicly, so it must not vary by, or carry, anything a cookie said.
        using var context = await _harness.ReadAsAsync(_world.A.Host, "/api/public/v1/site");
        using var contextWithCookies = await _harness.ReadAsAsync(_world.A.Host, "/api/public/v1/site", WithCookies);
        Assert.Equal(HttpStatusCode.OK, contextWithCookies.StatusCode);
        Assert.Equal(await context.Content.ReadAsStringAsync(), await contextWithCookies.Content.ReadAsStringAsync());
        Assert.Equal(context.Headers.CacheControl, contextWithCookies.Headers.CacheControl);
        Assert.DoesNotContain("Cookie", contextWithCookies.Headers.Vary, StringComparer.OrdinalIgnoreCase);
        Assert.False(contextWithCookies.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Without_the_cache_a_known_host_still_resolves_from_the_database()
    {
        var connection = await database.App.GetConnectionStringAsync(MigrationService.MigrationServiceExtensions.ConnectionName)
            ?? throw new InvalidOperationException("The test AppHost has no pantrypress connection string.");
        await using var site = SiteFactory.Over(connection, SiteFactory.UnreachableCache);
        var client = site.CreateReader();

        foreach (var host in new[] { _world.A.Host, _world.B.Host })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, SiteProbes.Path);
            request.Headers.Host = host;
            using var response = await client.SendAsync(request);
            var answer = await response.Content.ReadFromJsonAsync<SiteProbeAnswer>();
            Assert.Equal(host == _world.A.Host ? _world.A.Id.Value : _world.B.Id.Value, answer!.SiteId);
        }
    }

    private async Task<SiteProbeAnswer> ProbeAsync(string host)
    {
        using var response = await _harness.ReadAsAsync(host);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{host} answered {(int)response.StatusCode}.");
        return (await response.Content.ReadFromJsonAsync<SiteProbeAnswer>())!;
    }

    private static async Task AssertNotFoundAsync(HttpResponseMessage response, string? host = null)
    {
        Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{host} answered {(int)response.StatusCode}.");
        Assert.Equal("not-found", await response.CodeAsync());
    }

    private Task AddHostAsync(SiteId site, string host, SiteHostKind kind, SiteHostStatus status = SiteHostStatus.Active) =>
        _harness.ChangeAsync(db => db.Add(new SiteHost
        {
            Id = new SiteHostId(Guid.NewGuid()),
            SiteId = site,
            Host = host,
            Kind = kind,
            IsCanonical = false,
            Status = status,
            CreatedAt = AddedAt,
        }));

    // The canonical host moves in one statement, as the trigger requires (PP2.9).
    private Task MoveCanonicalAsync(SiteId site, string host) =>
        _harness.ChangeAsync(db => db.Set<SiteHost>()
            .Where(h => h.SiteId == site && (h.IsCanonical || h.Host == host))
            .ExecuteUpdate(set => set.SetProperty(h => h.IsCanonical, h => h.Host == host)));

    private async Task RefreshAsync(string host)
    {
        await using var scope = _harness.Scope();
        await scope.ServiceProvider.GetRequiredService<SiteHostMapDataLayer>().RefreshAsync([host]);
    }

    private async Task RefreshSiteAsync(SiteId site)
    {
        await using var scope = _harness.Scope();
        await scope.ServiceProvider.GetRequiredService<SiteHostMapDataLayer>().RefreshSiteAsync(site, CancellationToken.None);
    }

    private async Task<string> EntryAsync(string host)
    {
        var entry = await _redis.GetDatabase().HashGetAllAsync(PlatformCacheKey.For(SiteHostMap.Area, host).Value);
        return string.Join(";", entry.OrderBy(e => e.Name.ToString(), StringComparer.Ordinal).Select(e => $"{e.Name}={e.Value}"));
    }

    private static string MixedCase(string host) =>
        string.Concat(host.Select((c, i) => i % 2 == 0 ? char.ToUpperInvariant(c) : c));

    // A clock that moves on by a fixed step each time it is read, as a lookup that stalls between its read and its fill.
    private sealed class SteppingClock(TimeSpan step) : IClock
    {
        private DateTimeOffset _now = AddedAt;

        public DateTimeOffset UtcNow
        {
            get
            {
                var now = _now;
                _now += step;
                return now;
            }
        }
    }

    private sealed class CacheCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Add() => Interlocked.Increment(ref _count);

        public void Reset() => Interlocked.Exchange(ref _count, 0);
    }

    private sealed class CountingCache(IPlatformCache inner, CacheCounter counter) : IPlatformCache
    {
        public Task<CacheRead> ReadAsync(PlatformCacheKey key)
        {
            counter.Add();
            return inner.ReadAsync(key);
        }

        public Task<bool> WriteAsync(PlatformCacheKey key, CacheRead seen, string value, TimeSpan lifetime)
        {
            counter.Add();
            return inner.WriteAsync(key, seen, value, lifetime);
        }

        public Task InvalidateAsync(PlatformCacheKey key)
        {
            counter.Add();
            return inner.InvalidateAsync(key);
        }
    }
}
