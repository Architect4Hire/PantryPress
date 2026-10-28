using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Caching;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.DataLayer;
using PantryPress.Site.Controllers;
using PantryPress.Site.Hosting;
using PantryPress.Tests.Persistence;
using PantryPress.Tests.TwoSite;
using StackExchange.Redis;

namespace PantryPress.Tests.Site;

// Inside the namespace, so the types win over this namespace's own name.
using PantryPress.Domain.Modules.Sites;

// PP2.13 (SITE-008, SEO-007): the public site context, over the two-site harness. A test per status - open, coming soon,
// unavailable, gone - with its status code, code, Retry-After, and caching; the private statuses answered exactly like
// another; only what the reader's own Host names, through every host-header trick; and no SQL once the map is warm.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class SiteContextTests(MigratedDatabase database) : IAsyncLifetime
{
    private const string Path = "/" + PublicSurface.V1Route + "/site";

    private static readonly DateTimeOffset AddedAt = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    // One counter for the class: a new interceptor per test would build EF Core a new internal service provider each time,
    // and past twenty in one process EF Core refuses (ManyServiceProvidersCreatedWarning). The collection runs one test at
    // a time, and each resets it.
    private static readonly CommandCounter Queries = new();
    private TwoSiteHarness _harness = null!;
    private TwoSiteWorld _world = null!;
    private ConnectionMultiplexer _redis = null!;

    public async Task InitializeAsync()
    {
        _harness = await TwoSiteHarness.CreateAsync(
            database,
            withSite: true,
            siteServices: services => services.ConfigureDbContext<PantryPressDbContext>(options => options.AddInterceptors(Queries)));
        _world = await _harness.SeedAsync("context");

        // Look-alike sites, but each with a name of its own, so an answer that carried the other's would show.
        await RenameAsync(_world.A.Id, "Alpha Kitchen", "Soups | stews", "en-US");
        await RenameAsync(_world.B.Id, "Beta Bakes", "Bread, mostly", "fr-CA");
        _redis = await ConnectionMultiplexer.ConnectAsync(_harness.CacheConnection);
    }

    public async Task DisposeAsync()
    {
        await _redis.DisposeAsync();
        await _harness.DisposeAsync();
    }

    [Fact]
    public async Task A_live_site_answers_its_public_context_and_nothing_else()
    {
        using var response = await ReadAsync(_world.A.Host);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(SiteContextController.CacheControl, response.Headers.CacheControl?.ToString());
        Assert.Null(response.Headers.RetryAfter);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            ["canonicalHost", "isCanonical", "locale", "name", "tagline"],
            body.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            new PublicSiteDetail("Alpha Kitchen", "Soups | stews", "en-US", _world.A.Host, true),
            body.RootElement.Deserialize<PublicSiteDetail>(JsonSerializerOptions.Web));

        // No site id, slug, time zone, or status: the reader's Host already named the site, and the rest is private.
        var text = body.RootElement.GetRawText();
        Assert.DoesNotContain(_world.A.Id.Value.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("America/Chicago", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_read_only_site_answers_exactly_as_it_did_while_live()
    {
        using var live = await ReadAsync(_world.A.Host);
        var liveBody = await live.Content.ReadAsStringAsync();

        await SetStatusAsync(_world.A.Id, SiteStatus.ReadOnly);
        using var readOnly = await ReadAsync(_world.A.Host);

        Assert.Equal(HttpStatusCode.OK, readOnly.StatusCode);
        Assert.Equal(liveBody, await readOnly.Content.ReadAsStringAsync());
        Assert.Equal(Headers(live), Headers(readOnly));
    }

    public static TheoryData<SiteStatus, HttpStatusCode, string, int?> ClosedStatuses() => new()
    {
        { SiteStatus.Provisioning, HttpStatusCode.ServiceUnavailable, "site-coming-soon", 30 },
        { SiteStatus.Suspended, HttpStatusCode.ServiceUnavailable, "site-unavailable", 3600 },
        { SiteStatus.Offline, HttpStatusCode.ServiceUnavailable, "site-unavailable", 3600 },
        { SiteStatus.Deleted, HttpStatusCode.Gone, "site-gone", null },
    };

    [Theory]
    [MemberData(nameof(ClosedStatuses))]
    public async Task A_closed_site_answers_its_status_with_nothing_of_the_sites(
        SiteStatus status,
        HttpStatusCode expected,
        string code,
        int? retryAfterSeconds)
    {
        await SetStatusAsync(_world.A.Id, status);

        using var response = await ReadAsync(_world.A.Host);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(code, await response.CodeAsync());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(retryAfterSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null, response.Headers.RetryAfter?.Delta);

        // A neutral notice (SITE-008): not the site's name, tagline, address, or id.
        var text = await response.Content.ReadAsStringAsync();
        using (var problem = JsonDocument.Parse(text))
        {
            Assert.Equal(
                ["code", "status", "title", "traceId", "type"],
                problem.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            Assert.Equal((int)expected, problem.RootElement.GetProperty("status").GetInt32());
        }

        foreach (var leak in new[] { "Alpha", "Soups", _world.A.Slug, _world.A.Id.Value.ToString() })
        {
            Assert.DoesNotContain(leak, text, StringComparison.OrdinalIgnoreCase);
        }

        // The other site is open as before.
        using var other = await ReadAsync(_world.B.Host);
        Assert.Equal("Beta Bakes", (await other.Content.ReadFromJsonAsync<PublicSiteDetail>())!.Name);
    }

    [Fact]
    public async Task Suspended_and_offline_cannot_be_told_apart()
    {
        await SetStatusAsync(_world.A.Id, SiteStatus.Suspended);
        await SetStatusAsync(_world.B.Id, SiteStatus.Offline);

        using var suspended = await ReadAsync(_world.A.Host);
        using var offline = await ReadAsync(_world.B.Host);

        await TwoSiteChecks.AssertIndistinguishableAsync(suspended, offline, HttpStatusCode.ServiceUnavailable, "site-unavailable");
    }

    [Fact]
    public async Task A_deleted_site_with_no_canonical_host_left_is_gone()
    {
        await SetStatusAsync(_world.A.Id, SiteStatus.Deleted);
        await _harness.ChangeAsync(db => db.Set<SiteHost>()
            .Where(h => h.SiteId == _world.A.Id)
            .ExecuteUpdate(set => set.SetProperty(h => h.IsCanonical, false)));
        await RefreshSiteAsync(_world.A.Id);

        using var response = await ReadAsync(_world.A.Host);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("site-gone", await response.CodeAsync());
    }

    [Fact]
    public async Task A_status_change_is_answered_at_once_and_a_restore_is_immediate()
    {
        using (var before = await ReadAsync(_world.A.Host))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        await SetStatusAsync(_world.A.Id, SiteStatus.Suspended);
        using (var suspended = await ReadAsync(_world.A.Host))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, suspended.StatusCode);
        }

        await SetStatusAsync(_world.A.Id, SiteStatus.Live);
        using var restored = await ReadAsync(_world.A.Host);
        Assert.Equal("Alpha Kitchen", (await restored.Content.ReadFromJsonAsync<PublicSiteDetail>())!.Name);
    }

    [Fact]
    public async Task Each_host_answers_its_own_site_whatever_the_other_is()
    {
        await SetStatusAsync(_world.B.Id, SiteStatus.Suspended);

        using var a = await ReadAsync(_world.A.Host);
        using var b = await ReadAsync(_world.B.Host);

        Assert.Equal(new PublicSiteDetail("Alpha Kitchen", "Soups | stews", "en-US", _world.A.Host, true), await a.Content.ReadFromJsonAsync<PublicSiteDetail>());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, b.StatusCode);
        var bText = await b.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Alpha", bText, StringComparison.Ordinal);
        Assert.DoesNotContain("Beta", bText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("{0}.")]
    [InlineData("{0}:443")]
    [InlineData("{0}.:8443")]
    public async Task Case_a_trailing_dot_and_a_port_answer_the_same_site(string spelling)
    {
        foreach (var host in new[] { _world.A.Host, _world.A.Host.ToUpperInvariant() })
        {
            using var response = await ReadAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, spelling, host));
            Assert.Equal("Alpha Kitchen", (await response.Content.ReadFromJsonAsync<PublicSiteDetail>())!.Name);
        }
    }

    [Fact]
    public async Task A_forwarded_host_never_chooses_whose_context_is_answered()
    {
        foreach (var (name, value) in TwoSiteChecks.ForwardingTricks(_world.B.Host))
        {
            using var response = await ReadAsync(_world.A.Host, r => r.Headers.TryAddWithoutValidation(name, value));
            Assert.Equal("Alpha Kitchen", (await response.Content.ReadFromJsonAsync<PublicSiteDetail>())!.Name);
        }
    }

    [Fact]
    public async Task A_host_near_either_sites_answers_no_site_exactly_as_an_unknown_path_does()
    {
        using var unknownPath = await _harness.ReadAsAsync(_world.A.Host, "/api/public/v1/nothing-here");
        foreach (var host in TwoSiteChecks.HostTricks(_world))
        {
            using var response = await ReadAsync(host);
            await TwoSiteChecks.AssertIndistinguishableAsync(response, unknownPath, HttpStatusCode.NotFound, "not-found");
        }
    }

    [Fact]
    public async Task A_custom_domain_and_the_old_subdomain_say_where_the_site_lives()
    {
        var custom = $"bake-{Guid.NewGuid():N}.example";
        await _harness.ChangeAsync(db => db.Add(new SiteHost
        {
            Id = new SiteHostId(Guid.NewGuid()),
            SiteId = _world.B.Id,
            Host = custom,
            Kind = SiteHostKind.Custom,
            IsCanonical = false,
            Status = SiteHostStatus.Active,
            CreatedAt = AddedAt,
        }));
        await _harness.ChangeAsync(db => db.Set<SiteHost>()
            .Where(h => h.SiteId == _world.B.Id && (h.IsCanonical || h.Host == custom))
            .ExecuteUpdate(set => set.SetProperty(h => h.IsCanonical, h => h.Host == custom)));
        await RefreshSiteAsync(_world.B.Id);

        using var onCustom = await ReadAsync(custom);
        using var onSubdomain = await ReadAsync(_world.B.Host);

        // The answer reports, and does not redirect: the page's 301 is the SSR server's (PP7.13a).
        Assert.Equal(new PublicSiteDetail("Beta Bakes", "Bread, mostly", "fr-CA", custom, true), await onCustom.Content.ReadFromJsonAsync<PublicSiteDetail>());
        Assert.Equal(HttpStatusCode.OK, onSubdomain.StatusCode);
        Assert.Equal(new PublicSiteDetail("Beta Bakes", "Bread, mostly", "fr-CA", custom, false), await onSubdomain.Content.ReadFromJsonAsync<PublicSiteDetail>());
    }

    [Fact]
    public async Task A_closed_site_is_answered_before_any_redirect_it_would_owe()
    {
        var custom = $"cook-{Guid.NewGuid():N}.example";
        await _harness.ChangeAsync(db => db.Add(new SiteHost
        {
            Id = new SiteHostId(Guid.NewGuid()),
            SiteId = _world.A.Id,
            Host = custom,
            Kind = SiteHostKind.Custom,
            IsCanonical = false,
            Status = SiteHostStatus.Active,
            CreatedAt = AddedAt,
        }));
        await SetStatusAsync(_world.A.Id, SiteStatus.Suspended);

        using var onCanonical = await ReadAsync(_world.A.Host);
        using var offCanonical = await ReadAsync(custom);

        await TwoSiteChecks.AssertIndistinguishableAsync(offCanonical, onCanonical, HttpStatusCode.ServiceUnavailable, "site-unavailable");
    }

    [Fact]
    public async Task A_warm_site_is_answered_with_no_sql_whatever_its_status()
    {
        await SetStatusAsync(_world.B.Id, SiteStatus.Offline);
        using (await ReadAsync(_world.A.Host))
        using (await ReadAsync(_world.B.Host))
        {
        }

        Queries.Reset();
        for (var i = 0; i < 3; i++)
        {
            using var a = await ReadAsync(_world.A.Host);
            using var b = await ReadAsync(_world.B.Host);
            Assert.Equal(HttpStatusCode.OK, a.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, b.StatusCode);
        }

        Assert.Equal(0, Queries.Count);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Only_a_read_is_answered(string method)
    {
        using var response = await ReadAsync(_world.A.Host, r => r.Method = new HttpMethod(method));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("method-not-allowed", await response.CodeAsync());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task A_query_or_header_naming_a_site_changes_nothing()
    {
        using var response = await _harness.ReadAsAsync(
            _world.A.Host,
            $"{Path}?siteId={_world.B.Id.Value}&site={_world.B.Slug}",
            r => r.Headers.TryAddWithoutValidation("X-PP-Site", _world.B.Id.Value.ToString()));

        Assert.Equal("Alpha Kitchen", (await response.Content.ReadFromJsonAsync<PublicSiteDetail>())!.Name);
    }

    [Fact]
    public async Task The_development_document_describes_the_public_surface_on_a_sites_host()
    {
        using var response = await _harness.ReadAsAsync(_world.A.Host, "/openapi/public-v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.Equal([Path], paths.EnumerateObject().Select(p => p.Name));

        var answers = paths.GetProperty(Path).GetProperty("get").GetProperty("responses");
        Assert.Equal(["200", "404", "410", "500", "503"], answers.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.True(answers.GetProperty("503").GetProperty("headers").TryGetProperty("Retry-After", out _));

        // Nowhere but on a site's host: the document is behind host resolution like everything else.
        using var nowhere = await _harness.ReadAsAsync($"nobody-{Guid.NewGuid():N}.{TwoSiteHarness.SubdomainZone}", "/openapi/public-v1.json");
        Assert.Equal(HttpStatusCode.NotFound, nowhere.StatusCode);
    }

    [Fact]
    public async Task No_document_is_served_outside_development()
    {
        await using var deployed = new SiteFactory("Production");

        using var response = await deployed.CreateReader().GetAsync("/openapi/public-v1.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not-found", await response.CodeAsync());
    }

    [Fact]
    public async Task A_host_that_changes_hands_answers_its_new_sites_context_at_once()
    {
        var shared = $"moving-{Guid.NewGuid():N}.example";
        await AddHostAsync(_world.A.Id, shared);
        await RefreshAsync(shared);
        using (var onA = await ReadAsync(shared))
        {
            Assert.Equal(new PublicSiteDetail("Alpha Kitchen", "Soups | stews", "en-US", _world.A.Host, false), await onA.Content.ReadFromJsonAsync<PublicSiteDetail>());
        }

        await _harness.ChangeAsync(db => db.Set<SiteHost>().Where(h => h.Host == shared).ExecuteDelete());
        await RefreshAsync(shared);
        await AddHostAsync(_world.B.Id, shared);
        await RefreshAsync(shared);

        using var onB = await ReadAsync(shared);
        var text = await onB.Content.ReadAsStringAsync();
        Assert.Equal(new PublicSiteDetail("Beta Bakes", "Bread, mostly", "fr-CA", _world.B.Host, false), JsonSerializer.Deserialize<PublicSiteDetail>(text, JsonSerializerOptions.Web));
        Assert.DoesNotContain("Alpha", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rename_is_answered_at_once_after_its_refresh_and_the_other_sites_entry_is_untouched()
    {
        using (await ReadAsync(_world.A.Host))
        using (await ReadAsync(_world.B.Host))
        {
        }

        var bEntry = await EntryAsync(_world.B.Host);

        await RenameAsync(_world.A.Id, "Alpha Again", "", "de-DE");

        using var a = await ReadAsync(_world.A.Host);
        Assert.Equal(new PublicSiteDetail("Alpha Again", "", "de-DE", _world.A.Host, true), await a.Content.ReadFromJsonAsync<PublicSiteDetail>());
        Assert.Equal(bEntry, await EntryAsync(_world.B.Host));
        using var b = await ReadAsync(_world.B.Host);
        Assert.Equal("Beta Bakes", (await b.Content.ReadFromJsonAsync<PublicSiteDetail>())!.Name);
    }

    [Fact]
    public async Task Without_its_refresh_a_rename_is_not_seen_and_only_ever_on_its_own_site()
    {
        using (await ReadAsync(_world.A.Host))
        {
        }

        await _harness.ChangeAsync(db => db.Set<Domain.Modules.Sites.Site>()
            .Where(s => s.Id == _world.A.Id)
            .ExecuteUpdate(set => set.SetProperty(s => s.Name, "Not Yet")));

        using var a = await ReadAsync(_world.A.Host);
        using var b = await ReadAsync(_world.B.Host);
        Assert.Equal("Alpha Kitchen", (await a.Content.ReadFromJsonAsync<PublicSiteDetail>())!.Name);
        Assert.Equal("Beta Bakes", (await b.Content.ReadFromJsonAsync<PublicSiteDetail>())!.Name);

        await RefreshSiteAsync(_world.A.Id);
        using var refreshed = await ReadAsync(_world.A.Host);
        Assert.Equal("Not Yet", (await refreshed.Content.ReadFromJsonAsync<PublicSiteDetail>())!.Name);
    }

    [Theory]
    [InlineData(SiteStatus.Provisioning)]
    [InlineData(SiteStatus.Suspended)]
    [InlineData(SiteStatus.Offline)]
    [InlineData(SiteStatus.Deleted)]
    public async Task A_closed_sites_name_never_sits_in_the_shared_cache(SiteStatus status)
    {
        await SetStatusAsync(_world.A.Id, status);
        using (await ReadAsync(_world.A.Host))
        {
        }

        var entry = await EntryAsync(_world.A.Host);

        Assert.Contains(_world.A.Id.Value.ToString(), entry, StringComparison.Ordinal);
        Assert.DoesNotContain("Alpha", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("Soups", entry, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SiteStatus.Provisioning, "text/html")]
    [InlineData(SiteStatus.Suspended, "text/html")]
    [InlineData(SiteStatus.Suspended, "application/xml")]
    [InlineData(SiteStatus.Deleted, "text/plain")]
    public async Task A_status_answer_is_the_problem_document_whatever_the_reader_accepts(SiteStatus status, string accept)
    {
        await SetStatusAsync(_world.A.Id, status);
        using var plain = await ReadAsync(_world.A.Host);

        using var response = await ReadAsync(_world.A.Host, r => r.Headers.Accept.ParseAdd(accept));

        await TwoSiteChecks.AssertIndistinguishableAsync(response, plain, plain.StatusCode, (await plain.CodeAsync())!);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(plain.Headers.RetryAfter?.Delta, response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task An_unknown_path_on_a_closed_site_is_the_neutral_not_found()
    {
        await SetStatusAsync(_world.A.Id, SiteStatus.Suspended);

        using var unknownPath = await _harness.ReadAsAsync(_world.A.Host, "/api/public/v1/nothing-here");
        using var unknownHost = await _harness.ReadAsAsync($"nobody-{Guid.NewGuid():N}.{TwoSiteHarness.SubdomainZone}", "/api/public/v1/nothing-here");

        await TwoSiteChecks.AssertIndistinguishableAsync(unknownPath, unknownHost, HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task A_cold_site_is_read_once_and_then_from_the_map()
    {
        Queries.Reset();
        using (await ReadAsync(_world.B.Host))
        {
        }

        var cold = Queries.Count;
        using (await ReadAsync(_world.B.Host))
        {
        }

        Assert.Equal(1, cold);
        Assert.Equal(1, Queries.Count);
    }

    [Fact]
    public async Task A_row_the_create_validator_would_refuse_is_still_answered_from_the_map()
    {
        // An operator's edit or an import is not held to the create form's rules; only the columns bind it.
        await RenameAsync(_world.A.Id, "Two\nlines", "", "EN");
        using (await ReadAsync(_world.A.Host))
        {
        }

        Queries.Reset();
        using var response = await ReadAsync(_world.A.Host);

        Assert.Equal("Two\nlines", (await response.Content.ReadFromJsonAsync<PublicSiteDetail>())!.Name);
        Assert.Equal(0, Queries.Count);
    }

    private Task<HttpResponseMessage> ReadAsync(string host, Action<HttpRequestMessage>? configure = null) =>
        _harness.ReadAsAsync(host, Path, configure);

    private async Task SetStatusAsync(SiteId site, SiteStatus status)
    {
        await _harness.ChangeAsync(db => db.Set<Domain.Modules.Sites.Site>()
            .Where(s => s.Id == site)
            .ExecuteUpdate(set => set.SetProperty(s => s.Status, status)));
        await RefreshSiteAsync(site);
    }

    private async Task RenameAsync(SiteId site, string name, string tagline, string locale)
    {
        await _harness.ChangeAsync(db => db.Set<Domain.Modules.Sites.Site>()
            .Where(s => s.Id == site)
            .ExecuteUpdate(set => set
                .SetProperty(s => s.Name, name)
                .SetProperty(s => s.Tagline, tagline)
                .SetProperty(s => s.Locale, locale)));
        await RefreshSiteAsync(site);
    }

    private Task AddHostAsync(SiteId site, string host) =>
        _harness.ChangeAsync(db => db.Add(new SiteHost
        {
            Id = new SiteHostId(Guid.NewGuid()),
            SiteId = site,
            Host = host,
            Kind = SiteHostKind.Custom,
            IsCanonical = false,
            Status = SiteHostStatus.Active,
            CreatedAt = AddedAt,
        }));

    private async Task RefreshAsync(string host)
    {
        await using var scope = _harness.Scope();
        await scope.ServiceProvider.GetRequiredService<SiteHostMapDataLayer>().RefreshAsync([host]);
    }

    private async Task<string> EntryAsync(string host)
    {
        var entry = await _redis.GetDatabase().HashGetAllAsync(PlatformCacheKey.For(SiteHostMap.Area, host).Value);
        return string.Join(";", entry.OrderBy(e => e.Name.ToString(), StringComparer.Ordinal).Select(e => $"{e.Name}={e.Value}"));
    }

    // What a change of a site's status or public identity owes the host map (tenancy.md, "Tenant root").
    private async Task RefreshSiteAsync(SiteId site)
    {
        await using var scope = _harness.Scope();
        await scope.ServiceProvider.GetRequiredService<SiteHostMapDataLayer>().RefreshSiteAsync(site, CancellationToken.None);
    }

    private static string Headers(HttpResponseMessage response) => string.Join(
        "\n",
        response.Headers.Concat(response.Content.Headers)
            .Where(h => h.Key is not ("Date" or "Content-Length"))
            .OrderBy(h => h.Key, StringComparer.Ordinal)
            .Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));
}
