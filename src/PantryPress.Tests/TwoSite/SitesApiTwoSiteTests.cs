using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.ApiService.Hosting;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Tests.Api;
using PantryPress.Tests.Persistence;

namespace PantryPress.Tests.TwoSite;

// Inside the namespace, so the types win over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.DataLayer;

// PP2.10: creating, listing, and reading sites, and checking an address, over ApiService against the migrated schema,
// in the two-site world (tenancy.md, "Two-site test": what PP2.10 owes the tenant root). A list returns only the caller's
// sites; reading another site by slug answers like no site; a create leaves both look-alike sites' rows as they were; and
// neither availability nor a refused create ever says why an address is taken, let alone by whom (SITE-002).
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class SitesApiTwoSiteTests(MigratedDatabase database) : IAsyncLifetime
{
    private const string Sites = "/api/v1/sites";
    private const string Availability = "/api/v1/slug-availability";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    private TwoSiteHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TwoSiteHarness.CreateAsync(database, withApi: true);

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task Creating_a_site_writes_it_its_owner_and_its_subdomain_together_and_audits_it()
    {
        var world = await _harness.SeedAsync("create");
        var creator = TwoSiteHarness.NewAccount();
        var slug = Fresh("pie");
        using var client = _harness.ClientFor(creator);

        HttpResponseMessage? response = null;
        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, () =>
            TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.B.Id, async () =>
                response = await client.PostAsJsonAsync(Sites, Request(slug.ToUpperInvariant()) with { Name = "  Pie Club  " })));

        using (response)
        {
            Assert.Equal(HttpStatusCode.Created, response!.StatusCode);
            Assert.Equal($"/api/v1/sites/{slug}", response.Headers.Location?.OriginalString);
            var created = await response.Content.ReadFromJsonAsync<SiteDetail>(Json);
            Assert.Equal(
                new SiteDetail(slug, "Pie Club", "Weeknight dinners", SiteStatus.Live, "America/Chicago", "en-US", created!.CreatedAt, SiteRole.Owner, $"{slug}.pantrypress.app"),
                created);
        }

        await using var scope = _harness.Scope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var site = await db.Set<Site>().AsNoTracking().SingleAsync(s => s.Slug == slug);
        Assert.NotEqual(world.A.Id, site.Id);
        Assert.NotEqual(world.B.Id, site.Id);
        var member = await db.Set<SiteMember>().AsNoTracking().SingleAsync(m => m.SiteId == site.Id);
        Assert.Equal((creator, SiteRole.Owner, SiteMemberStatus.Active), (member.AccountId, member.Role, member.Status));
        var host = await db.Set<SiteHost>().AsNoTracking().SingleAsync(h => h.SiteId == site.Id);
        Assert.Equal(
            ($"{slug}.pantrypress.app", SiteHostKind.Subdomain, true, SiteHostStatus.Active),
            (host.Host, host.Kind, host.IsCanonical, host.Status));
        var audit = await db.Set<AuditEntry>().AsNoTracking().SingleAsync(e => e.SiteId == site.Id.Value);
        Assert.Equal(
            (SiteAudit.Created, creator.Value, SiteAudit.TargetKind, site.Id.ToString()),
            (audit.Action, audit.ActorId!.Value, audit.TargetKind, audit.TargetId));
    }

    // Held by A, the same in another case, held by a deleted site, reserved, and still answering as a site's address: one
    // answer for each, byte for byte, from create and from availability, so nothing tells a taken address from a reserved
    // one or names whose it is.
    [Fact]
    public async Task Every_unavailable_address_gets_the_same_answer()
    {
        var world = await _harness.SeedAsync("taken");
        var gone = await _harness.SeedAsync("gone");
        await _harness.ChangeAsync(db => db.Set<Site>().Single(s => s.Id == gone.A.Id).Status = SiteStatus.Deleted);
        var hostOnly = Fresh("held");
        await _harness.ChangeAsync(db => db.Add(new SiteHost
        {
            Id = new SiteHostId(Guid.NewGuid()),
            SiteId = world.A.Id,
            Host = $"{hostOnly}.pantrypress.app",
            Kind = SiteHostKind.Custom,
            Status = SiteHostStatus.Pending,
            CreatedAt = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero),
        }));

        using var client = _harness.ClientFor(world.B.Owner);
        string[] unavailable = [world.A.Slug, world.A.Slug.ToUpperInvariant(), gone.A.Slug, "app", "www", hostOnly];
        using var reserved = await client.PostAsJsonAsync(Sites, Request("app"));
        var reservedCheck = await AnswerAsync(await client.GetAsync($"{Availability}?slug=app"));
        foreach (var slug in unavailable)
        {
            await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, async () =>
            {
                using var refused = await client.PostAsJsonAsync(Sites, Request(slug));
                await TwoSiteChecks.AssertIndistinguishableAsync(refused, reserved, HttpStatusCode.Conflict, SiteErrors.SlugUnavailable.Code);
            });

            // The whole answer, headers included, is the reserved slug's but for the slug itself.
            using var check = await client.GetAsync($"{Availability}?slug={slug}");
            Assert.Equal(HttpStatusCode.OK, check.StatusCode);
            var answer = await AnswerAsync(check);
            Assert.Equal(new SlugAvailability(slug.ToLowerInvariant(), false), JsonSerializer.Deserialize<SlugAvailability>(answer.Body, Json));
            Assert.Equal(reservedCheck.Body.Replace("\"app\"", $"\"{slug.ToLowerInvariant()}\"", StringComparison.Ordinal), answer.Body);
            Assert.Equal(reservedCheck.Headers, answer.Headers);
        }

        var free = Fresh("free");
        using var open = await client.GetAsync($"{Availability}?slug={free}");
        Assert.Equal(new SlugAvailability(free, true), await open.Content.ReadFromJsonAsync<SlugAvailability>(Json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("pi")]
    [InlineData("-pie")]
    [InlineData("pie--club")]
    [InlineData("pіe")]
    public async Task An_address_out_of_shape_is_a_slug_field_error(string slug)
    {
        using var client = _harness.ClientFor(TwoSiteHarness.NewAccount());

        using var check = await client.GetAsync($"{Availability}?slug={Uri.EscapeDataString(slug)}");
        using var create = await client.PostAsJsonAsync(Sites, Request(slug));

        foreach (var response in new[] { check, create })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("validation-failed", problem.RootElement.GetProperty("code").GetString());
            Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("slug", out _));
        }
    }

    // Each account lists exactly the sites it is an active member of, with its own role on each: the shared account
    // carries a different role into each site, and the account suspended on A sees only B.
    [Fact]
    public async Task A_list_holds_only_the_callers_own_sites()
    {
        var world = await _harness.SeedAsync("list");
        var expected = new Dictionary<AccountId, (SeededSite Site, SiteRole Role)[]>
        {
            [world.A.Owner] = [(world.A, SiteRole.Owner)],
            [world.A.Editor] = [(world.A, SiteRole.Editor)],
            [world.A.Author] = [(world.A, SiteRole.Author)],
            [world.A.Suspended] = [],
            [world.B.Owner] = [(world.B, SiteRole.Owner)],
            [world.Shared] = [(world.A, SiteRole.Author), (world.B, SiteRole.Owner)],
            [world.SuspendedOnA] = [(world.B, SiteRole.Editor)],
            [world.Stranger] = [],
        };

        foreach (var (account, sites) in expected)
        {
            using var client = _harness.ClientFor(account);
            var page = await client.GetFromJsonAsync<SitePage>(Sites, Json);
            Assert.Equal(
                sites.Select(s => new SiteSummary(s.Site.Slug, "Sunday Supper", SiteStatus.Live, s.Role, s.Site.Host)),
                page!.Items);
            Assert.Null(page.NextCursor);
        }
    }

    [Fact]
    public async Task A_list_pages_by_slug_and_leaves_out_deleted_sites()
    {
        var world = await _harness.SeedAsync("pages");
        using var client = _harness.ClientFor(world.Shared);

        var first = await client.GetFromJsonAsync<SitePage>($"{Sites}?limit=1", Json);
        Assert.Equal([world.A.Slug], first!.Items.Select(s => s.Slug));
        Assert.NotNull(first.NextCursor);

        var second = await client.GetFromJsonAsync<SitePage>($"{Sites}?limit=1&cursor={first.NextCursor}", Json);
        Assert.Equal([world.B.Slug], second!.Items.Select(s => s.Slug));
        Assert.Null(second.NextCursor);

        await _harness.ChangeAsync(db => db.Set<Site>().Single(s => s.Id == world.A.Id).Status = SiteStatus.Deleted);
        var after = await client.GetFromJsonAsync<SitePage>(Sites, Json);
        Assert.Equal([world.B.Slug], after!.Items.Select(s => s.Slug));

        // Every refusal names the parameter it is about, whether the facade or the binder refused it.
        foreach (var (query, field) in new[]
        {
            ("limit=101", "limit"),
            ("limit=0", "limit"),
            ("limit=abc", "limit"),
            ("limit=1.5", "limit"),
            ("cursor=not-a-cursor", "cursor"),
        })
        {
            using var refused = await client.GetAsync($"{Sites}?{query}");
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
            Assert.Equal([field], problem.RootElement.GetProperty("errors").EnumerateObject().Select(e => e.Name));
        }
    }

    // A cursor is only where to continue the caller's own list: one made from another site's slug lists nothing of it.
    [Fact]
    public async Task A_cursor_grants_nothing()
    {
        var world = await _harness.SeedAsync("cursor");
        using var client = _harness.ClientFor(world.B.Owner);

        var fromA = Domain.Modules.Sites.Business.SiteListCursor.After(world.A.Slug);
        var page = await client.GetFromJsonAsync<SitePage>($"{Sites}?cursor={fromA}", Json);

        Assert.Equal([world.B.Slug], page!.Items.Select(s => s.Slug));
    }

    // Every member reads the site with their own role; a suspended member, a stranger, and the look-alike site's owner
    // all get the answer a site that does not exist gets.
    [Fact]
    public async Task Reading_a_site_is_for_its_active_members_and_looks_like_nothing_to_anyone_else()
    {
        var world = await _harness.SeedAsync("read");
        foreach (var (account, role) in new[]
        {
            (world.A.Owner, SiteRole.Owner),
            (world.A.Editor, SiteRole.Editor),
            (world.A.Author, SiteRole.Author),
            (world.Shared, SiteRole.Author),
        })
        {
            using var client = _harness.ClientFor(account);
            var detail = await client.GetFromJsonAsync<SiteDetail>($"{Sites}/{world.A.Slug.ToUpperInvariant()}", Json);
            Assert.Equal((world.A.Slug, role, world.A.Host), (detail!.Slug, detail.Role, detail.Host));
        }

        // The shared account carries neither site's role into the other's read.
        using (var shared = _harness.ClientFor(world.Shared))
        {
            var b = await shared.GetFromJsonAsync<SiteDetail>($"{Sites}/{world.B.Slug}", Json);
            Assert.Equal((world.B.Slug, SiteRole.Owner, world.B.Host), (b!.Slug, b.Role, b.Host));
        }

        foreach (var account in new[] { world.A.Suspended, world.SuspendedOnA, world.Stranger, world.B.Owner, world.B.Editor })
        {
            using var client = _harness.ClientFor(account);
            foreach (var slug in new[] { world.A.Slug, world.A.Slug.ToUpperInvariant() })
            {
                using var other = await client.GetAsync($"{Sites}/{slug}");
                using var none = await client.GetAsync($"{Sites}/{Fresh("none")}");
                await TwoSiteChecks.AssertIndistinguishableAsync(other, none, HttpStatusCode.NotFound, SiteErrors.SiteNotFound.Code);
            }
        }

        // A deleted site is no site, to its own owner too.
        await _harness.ChangeAsync(db => db.Set<Site>().Single(s => s.Id == world.A.Id).Status = SiteStatus.Deleted);
        using var owner = _harness.ClientFor(world.A.Owner);
        using var deleted = await owner.GetAsync($"{Sites}/{world.A.Slug}");
        using var missing = await owner.GetAsync($"{Sites}/{Fresh("none")}");
        await TwoSiteChecks.AssertIndistinguishableAsync(deleted, missing, HttpStatusCode.NotFound, SiteErrors.SiteNotFound.Code);
    }

    // A body naming another site is not how a site is chosen: the new site gets a fresh id, and B is untouched.
    [Fact]
    public async Task A_site_id_in_the_body_names_nothing()
    {
        var world = await _harness.SeedAsync("forged");
        var slug = Fresh("forged");
        using var client = _harness.ClientFor(world.B.Owner);

        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.B.Id, async () =>
        {
            using var response = await client.PostAsJsonAsync(Sites, new
            {
                siteId = world.B.Id.Value,
                id = world.B.Id.Value,
                slug,
                name = "Forged",
                timeZone = "America/Chicago",
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        });

        await using var scope = _harness.Scope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        Assert.NotEqual(world.B.Id, (await db.Set<Site>().AsNoTracking().SingleAsync(s => s.Slug == slug)).Id);
    }

    // A double-clicked "Create" with one key makes one site and gets the first answer back.
    [Fact]
    public async Task A_retried_create_with_its_key_makes_one_site()
    {
        var slug = Fresh("again");
        using var client = _harness.ClientFor(TwoSiteHarness.NewAccount());
        var key = Guid.NewGuid().ToString("N");

        using var first = await PostWithKeyAsync(client, key, Request(slug));
        using var replay = await PostWithKeyAsync(client, key, Request(slug));
        using var changed = await PostWithKeyAsync(client, key, Request(slug) with { Name = "Another" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues(ApiIdempotency.ReplayedHeader)));
        Assert.Equal(first.Headers.Location, replay.Headers.Location);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, changed.StatusCode);

        await using var scope = _harness.Scope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        Assert.Equal(1, await db.Set<Site>().CountAsync(s => s.Slug == slug));
    }

    // Keys belong to the caller: another account sending the same key and body is a request of its own, never a replay of
    // the first account's new site.
    [Fact]
    public async Task One_accounts_key_replays_nothing_to_another()
    {
        var world = await _harness.SeedAsync("keys");
        var key = Guid.NewGuid().ToString("N");
        var slug = Fresh("keyed");
        using var first = _harness.ClientFor(world.Shared);
        using var second = _harness.ClientFor(world.B.Owner);

        using var created = await PostWithKeyAsync(first, key, Request(slug));
        using var other = await PostWithKeyAsync(second, key, Request(slug));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Equal(SiteErrors.SlugUnavailable.Code, await other.CodeAsync());
        Assert.False(other.Headers.Contains(ApiIdempotency.ReplayedHeader));
        Assert.Null(other.Headers.Location);
    }

    // Two sign-ups racing for one address: one gets it, the other the same answer as any taken address, and no half of
    // the loser's site is left behind.
    [Fact]
    public async Task Two_creates_racing_for_one_address_make_one_site()
    {
        var slug = Fresh("race");
        var accounts = Enumerable.Range(0, 6).Select(_ => TwoSiteHarness.NewAccount()).ToList();
        var clients = accounts.Select(_harness.ClientFor).ToList();

        var responses = await Task.WhenAll(clients.Select(c => c.PostAsJsonAsync(Sites, Request(slug))));

        // Each loser's answer is the one any taken address gets, whether the check or the save turned it away.
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        using var reference = await clients[0].PostAsJsonAsync(Sites, Request("app"));
        foreach (var refused in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            await TwoSiteChecks.AssertIndistinguishableAsync(refused, reference, HttpStatusCode.Conflict, SiteErrors.SlugUnavailable.Code);
        }

        await using var scope = _harness.Scope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var site = await db.Set<Site>().AsNoTracking().SingleAsync(s => s.Slug == slug);
        var owner = Assert.Single(await db.Set<SiteMember>().AsNoTracking().Where(m => m.SiteId == site.Id).ToListAsync());
        Assert.Contains(owner.AccountId, accounts);
        Assert.Single(await db.Set<SiteHost>().AsNoTracking().Where(h => h.SiteId == site.Id).ToListAsync());

        // One entry, for the winner's site, and none filed by a loser.
        var actors = accounts.Select(a => (Guid?)a.Value).ToList();
        var created = await db.Set<AuditEntry>().AsNoTracking()
            .Where(e => e.Action == SiteAudit.Created && actors.Contains(e.ActorId))
            .ToListAsync();
        var entry = Assert.Single(created);
        Assert.Equal((site.Id.Value, owner.AccountId.Value), (entry.SiteId!.Value, entry.ActorId!.Value));
        foreach (var response in responses)
        {
            response.Dispose();
        }

        foreach (var client in clients)
        {
            client.Dispose();
        }
    }

    // The save itself is the last word on a race the check cannot see: a slug held, a host held, and a slug reserved
    // only in the database (the code's list behind the migrations) are each "unavailable", never the database's own
    // message; any other failure is a bug and is thrown.
    [Fact]
    public async Task A_save_that_loses_the_address_says_unavailable_and_any_other_failure_throws()
    {
        var world = await _harness.SeedAsync("save");
        var hostOnly = Fresh("hosted");
        await _harness.ChangeAsync(db => db.Add(new SiteHost
        {
            Id = new SiteHostId(Guid.NewGuid()),
            SiteId = world.A.Id,
            Host = $"{hostOnly}.pantrypress.app",
            Kind = SiteHostKind.Custom,
            Status = SiteHostStatus.Pending,
            CreatedAt = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero),
        }));

        foreach (var slug in new[] { world.A.Slug, hostOnly, "app" })
        {
            await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, async () =>
                Assert.Equal(NewSiteOutcome.Unavailable, await StageAndSaveAsync(new SiteId(Guid.NewGuid()), slug, $"{slug}.pantrypress.app")));
        }

        var free = Fresh("broken");
        await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
            StageAndSaveAsync(new SiteId(Guid.NewGuid()), free, $"{free}.pantrypress.app", name: new string('n', 61)));
        await using var scope = _harness.Scope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<Site>().AnyAsync(s => s.Slug == free));
    }

    // A commit that landed before its connection failed meets its own rows when the save is retried: that is the caller's
    // site saved, never "unavailable" - which a stored idempotent answer would otherwise repeat for a day.
    [Fact]
    public async Task A_save_that_meets_its_own_landed_site_is_a_save()
    {
        var id = new SiteId(Guid.NewGuid());
        var slug = Fresh("landed");

        Assert.Equal(NewSiteOutcome.Saved, await StageAndSaveAsync(id, slug, $"{slug}.pantrypress.app"));
        Assert.Equal(NewSiteOutcome.Saved, await StageAndSaveAsync(id, slug, $"{slug}.pantrypress.app"));

        // The same slug under another id is still somebody else's.
        Assert.Equal(NewSiteOutcome.Unavailable, await StageAndSaveAsync(new SiteId(Guid.NewGuid()), slug, $"{slug}.pantrypress.app"));
    }

    // PP2.14: under an owned-site limit the save opens its own transaction inside the retry strategy. A commit that fails
    // and rolls back is retried from the top, and the retry must save the site again - never commit an empty transaction
    // and call the lost site saved, which a stored idempotent answer would then repeat for a day.
    [Fact]
    public async Task A_commit_that_failed_under_a_limit_is_saved_again_on_the_retry()
    {
        var commits = new PlantedCommitFault();
        await using var retrying = await TwoSiteHarness.CreateAsync(database, configure: commits.Configure);
        var world = await _harness.SeedAsync("retried");
        var id = new SiteId(Guid.NewGuid());
        var slug = Fresh("retried");
        var owner = TwoSiteHarness.NewAccount();

        var outcome = NewSiteOutcome.Unavailable;
        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.B.Id, async () =>
            outcome = await StageAndSaveAsync(id, slug, $"{slug}.pantrypress.app", owner: owner, limit: new OwnedSiteLimit(owner, 1), harness: retrying));
        Assert.Equal(NewSiteOutcome.Saved, outcome);

        Assert.Equal(2, commits.Attempts);
        await using var scope = _harness.Scope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        Assert.True(await db.Set<Site>().AnyAsync(s => s.Id == id && s.Slug == slug));
        Assert.True(await db.Set<SiteMember>().AnyAsync(m => m.SiteId == id && m.AccountId == owner && m.Role == SiteRole.Owner));
        Assert.True(await db.Set<SiteHost>().AnyAsync(h => h.SiteId == id && h.IsCanonical));
    }

    // PP2.14: every request to a route that names a site is counted per account before the site policy looks the caller
    // up, so probing other sites' addresses - each a refused lookup the route's own allowance never sees - is bounded too.
    [Fact]
    public async Task Site_lookups_are_counted_per_account_before_the_policy_refuses_them()
    {
        await using var harness = await TwoSiteHarness.CreateAsync(
            database,
            withApi: true,
            apiSettings: new Dictionary<string, string?> { ["RateLimits:SiteLookupsPerAccountPerMinute"] = "3" });
        var world = await harness.SeedAsync("lookups");
        using var prober = harness.ClientFor(TwoSiteHarness.NewAccount());
        using var bystander = harness.ClientFor(TwoSiteHarness.NewAccount());

        var answers = new List<HttpStatusCode>();
        foreach (var slug in new[] { world.A.Slug, world.B.Slug, Fresh("none"), world.A.Slug })
        {
            using var response = await prober.GetAsync($"{Sites}/{slug}");
            answers.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Assert.NotNull(response.Headers.RetryAfter);
                Assert.Equal("too-many-requests", await response.CodeAsync());
            }
        }

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests], answers);

        // The refusal is the same for an address no site holds: the window is the account's alone, so a 429 says nothing
        // about whether a site lives there.
        using var none = await prober.GetAsync($"{Sites}/{Fresh("none")}");
        Assert.Equal(HttpStatusCode.TooManyRequests, none.StatusCode);
        Assert.Equal("too-many-requests", await none.CodeAsync());
        Assert.NotNull(none.Headers.RetryAfter);

        // A caller that accepts only a page still gets the problem document, never a bodiless refusal.
        using var asPage = new HttpRequestMessage(HttpMethod.Get, $"{Sites}/{world.A.Slug}");
        asPage.Headers.Accept.ParseAdd("text/html");
        using var refusedPage = await prober.SendAsync(asPage);
        Assert.Equal(HttpStatusCode.TooManyRequests, refusedPage.StatusCode);
        Assert.Equal("too-many-requests", await refusedPage.CodeAsync());

        // Another account has its own window, and a route that names no site is not counted here.
        using var other = await bystander.GetAsync($"{Sites}/{world.A.Slug}");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        using var list = await prober.GetAsync(Sites);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task Every_route_needs_a_signed_in_caller()
    {
        using var anonymous = _harness.ClientFor(TwoSiteHarness.NewAccount());
        anonymous.DefaultRequestHeaders.Remove(ApiServiceFactory.TestAccountHeader);

        var world = await _harness.SeedAsync("anon");

        using var create = await anonymous.PostAsJsonAsync(Sites, Request(Fresh("anon")));
        using var list = await anonymous.GetAsync(Sites);
        using var check = await anonymous.GetAsync($"{Availability}?slug=pie");
        using var read = await anonymous.GetAsync($"{Sites}/{world.A.Slug}");
        using var readNone = await anonymous.GetAsync($"{Sites}/{Fresh("none")}");

        Assert.All(new[] { create, list, check }, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        await TwoSiteChecks.AssertIndistinguishableAsync(read, readNone, HttpStatusCode.Unauthorized, "sign-in-required");
    }

    [Fact]
    public async Task Asking_without_an_address_names_the_slug_field()
    {
        using var client = _harness.ClientFor(TwoSiteHarness.NewAccount());

        using var response = await client.GetAsync(Availability);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["slug"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(e => e.Name));
    }

    private static CreateSiteRequest Request(string slug) =>
        new(slug, "Sunday Supper", "Weeknight dinners", "America/Chicago", null);

    // A slug no other test shares: a prefix, a hyphen, and a fresh id's first characters.
    private static string Fresh(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 21, SiteLimits.MaxSlugLength)];

    private static async Task<HttpResponseMessage> PostWithKeyAsync(HttpClient client, string key, CreateSiteRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Sites) { Content = JsonContent.Create(request) };
        message.Headers.TryAddWithoutValidation(ApiIdempotency.KeyHeader, key);
        return await client.SendAsync(message);
    }

    // The body and the headers but the date and length, which differ between any two answers.
    private static async Task<(string Body, string Headers)> AnswerAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var headers = response.Headers.Concat(response.Content.Headers)
                .Where(h => h.Key is not ("Date" or "Content-Length"))
                .OrderBy(h => h.Key, StringComparer.Ordinal)
                .Select(h => $"{h.Key}: {string.Join(",", h.Value)}");
            return (await response.Content.ReadAsStringAsync(), string.Join("\n", headers));
        }
    }

    // Stages a whole new site past the facade's check, as a second request that passed it at the same moment would.
    private async Task<NewSiteOutcome> StageAndSaveAsync(
        SiteId id,
        string slug,
        string host,
        string name = "Racer",
        AccountId? owner = null,
        OwnedSiteLimit? limit = null,
        TwoSiteHarness? harness = null)
    {
        await using var scope = (harness ?? _harness).Scope();
        var data = scope.ServiceProvider.GetRequiredService<SitesDataLayer>();
        var now = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        data.StageNewSite(
            new Site { Id = id, Slug = slug, Name = name, Status = SiteStatus.Live, TimeZone = "UTC", Locale = "en-US", CreatedAt = now },
            new SiteMember { SiteId = id, AccountId = owner ?? TwoSiteHarness.NewAccount(), Role = SiteRole.Owner, CreatedAt = now },
            new SiteHost { Id = new SiteHostId(Guid.NewGuid()), SiteId = id, Host = host, Kind = SiteHostKind.Subdomain, IsCanonical = true, CreatedAt = now });
        return await data.SaveNewSiteAsync(id, slug, host, limit, CancellationToken.None);
    }
}
