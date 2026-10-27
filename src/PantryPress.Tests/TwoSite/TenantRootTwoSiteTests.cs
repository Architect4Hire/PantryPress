using System.Net;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Tests.Api;
using PantryPress.Tests.Persistence;

namespace PantryPress.Tests.TwoSite;

// Inside the namespace, so the types win over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;

// PP2.7: the two-site harness applied to the tenant root, Site, SiteMember, and SiteHost (PP2.9) (tenancy.md, "Tenant
// root"; ARCH-015, PSEC-007). None of the tables has a filter behind it, so these are the checks that stand in for one:
// every account sees exactly its own standing on each of two look-alike sites, the request's lookup cache never carries an
// answer across accounts, sites, or requests, one site's owners and hosts never count toward the other's and a write about
// one leaves the other's rows alone, and a stranger's answer about a real site is the answer for no site at all.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class TenantRootTwoSiteTests(MigratedDatabase database) : IAsyncLifetime
{
    private const int UniqueIndexViolation = 2601;
    private const int CheckConstraintViolation = 547;

    private TwoSiteHarness _harness = null!;

    private static CancellationToken Timeout => CancellationToken.None;

    public async Task InitializeAsync() => _harness = await TwoSiteHarness.CreateAsync(database, withApi: true);

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task Every_account_sees_exactly_its_own_standing_on_each_site()
    {
        var world = await _harness.SeedAsync();

        foreach (var (account, site, role) in Standings(world))
        {
            foreach (var slug in new[] { site.Slug, site.Slug.ToUpperInvariant() })
            {
                await using var scope = _harness.Scope();
                var found = await Memberships(scope).FindAsync(account, slug, Timeout);
                Assert.Equal(role is null ? null : new SiteMembership(site.Id, role.Value), found);

                var decision = await Memberships(scope).AuthorizeAsync(account, slug, SiteRole.Author, Timeout);
                Assert.Equal(role is not null, decision.IsSuccess);
                Assert.Equal(role is null ? null : site.Id, Context(scope).Current);
            }
        }
    }

    // The shared account holds Author on A and Owner on B: each site gets its own role, and a refusal enters nothing.
    [Fact]
    public async Task An_account_on_both_sites_carries_neither_sites_role_into_the_other()
    {
        var world = await _harness.SeedAsync();

        await using (var scope = _harness.Scope())
        {
            var refused = await Memberships(scope).AuthorizeAsync(world.Shared, world.A.Slug, SiteRole.Owner, Timeout);
            Assert.Equal(SiteErrors.SiteRoleRequired, refused.Error);
            Assert.Null(Context(scope).Current);
        }

        await using (var scope = _harness.Scope())
        {
            var admitted = await Memberships(scope).AuthorizeAsync(world.Shared, world.B.Slug, SiteRole.Owner, Timeout);
            Assert.Equal(new SiteMembership(world.B.Id, SiteRole.Owner), admitted.Value);
            Assert.Equal(world.B.Id, Context(scope).Current);
        }
    }

    // One request asks about many accounts and slugs; its lookup cache keys by both, so no answer is handed to the wrong
    // question, whatever order they come in and whatever case the slug is in.
    [Fact]
    public async Task The_request_cache_never_answers_one_question_with_anothers()
    {
        var world = await _harness.SeedAsync();
        await using var scope = _harness.Scope();
        var memberships = Memberships(scope);

        Assert.Equal(new SiteMembership(world.A.Id, SiteRole.Owner), await memberships.FindAsync(world.A.Owner, world.A.Slug, Timeout));
        Assert.Null(await memberships.FindAsync(world.B.Owner, world.A.Slug, Timeout));
        Assert.Null(await memberships.FindAsync(world.A.Owner, world.B.Slug, Timeout));
        Assert.Null(await memberships.FindAsync(world.Stranger, world.A.Slug.ToUpperInvariant(), Timeout));
        Assert.Null(await memberships.FindAsync(world.SuspendedOnA, world.A.Slug, Timeout));
        Assert.Equal(new SiteMembership(world.B.Id, SiteRole.Editor), await memberships.FindAsync(world.SuspendedOnA, world.B.Slug, Timeout));
        Assert.Equal(new SiteMembership(world.A.Id, SiteRole.Author), await memberships.FindAsync(world.Shared, world.A.Slug.ToUpperInvariant(), Timeout));
        Assert.Equal(new SiteMembership(world.B.Id, SiteRole.Owner), await memberships.FindAsync(world.Shared, world.B.Slug, Timeout));
        Assert.Equal(new SiteMembership(world.A.Id, SiteRole.Author), await memberships.FindAsync(world.Shared, world.A.Slug, Timeout));
    }

    // The lookup lives for one request only (auth.md): a membership suspended between two requests - through the facade
    // or over HTTP - is gone from the next, which answers as if the site were not there.
    [Fact]
    public async Task The_request_cache_never_outlives_its_request()
    {
        var world = await _harness.SeedAsync();
        using var client = _harness.ClientFor(world.B.Editor);

        await using (var scope = _harness.Scope())
        {
            Assert.NotNull(await Memberships(scope).FindAsync(world.B.Editor, world.B.Slug, Timeout));
        }

        using (var before = await client.GetAsync(SitePolicyProbeController.PathFor(world.B.Slug, "author"), Timeout))
        {
            Assert.Equal(HttpStatusCode.NoContent, before.StatusCode);
        }

        await _harness.ChangeAsync(db => db.Set<SiteMember>()
            .Single(m => m.SiteId == world.B.Id && m.AccountId == world.B.Editor).Status = SiteMemberStatus.Suspended);

        await using (var scope = _harness.Scope())
        {
            Assert.Null(await Memberships(scope).FindAsync(world.B.Editor, world.B.Slug, Timeout));
        }

        using var after = await client.GetAsync(SitePolicyProbeController.PathFor(world.B.Slug, "author"), Timeout);
        using var none = await client.GetAsync(SitePolicyProbeController.PathFor($"never-{Guid.NewGuid():N}", "author"), Timeout);
        await TwoSiteChecks.AssertIndistinguishableAsync(after, none, HttpStatusCode.NotFound, SiteErrors.SiteNotFound.Code);
    }

    // A has one owner; the shared account owns B. Demoting A's owner would leave A with none, so it is refused, though
    // the shared account is an owner - of B - and B's rows are untouched. B, with two, may lose one, and A's rows are
    // untouched by that.
    [Fact]
    public async Task One_sites_owners_never_count_toward_the_others()
    {
        var world = await _harness.SeedAsync();

        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.B.Id, async () =>
        {
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => _harness.ChangeAsync(db => db.Set<SiteMember>()
                .Single(m => m.SiteId == world.A.Id && m.AccountId == world.A.Owner).Role = SiteRole.Editor));
            Assert.Equal(SiteMemberConfiguration.KeepAnOwnerErrorNumber, Assert.IsType<SqlException>(error.InnerException).Number);
        });

        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, () => _harness.ChangeAsync(db => db.Set<SiteMember>()
            .Single(m => m.SiteId == world.B.Id && m.AccountId == world.B.Owner).Role = SiteRole.Editor));

        Assert.Equal([world.A.Owner], await OwnersAsync(world.A.Id));
        Assert.Equal([world.Shared], await OwnersAsync(world.B.Id));
    }

    // One statement over one account's memberships on both sites - as account erasure will write it (PP19.11a) - for an
    // account that is the sole owner of B: refused whole, so its membership on A stays as well as B's.
    [Fact]
    public async Task One_write_across_both_sites_for_one_account_is_refused_whole()
    {
        var world = await _harness.SeedAsync();
        await _harness.ChangeAsync(db => db.Set<SiteMember>()
            .Single(m => m.SiteId == world.B.Id && m.AccountId == world.B.Owner).Role = SiteRole.Editor);

        foreach (var write in new Func<PantryPressDbContext, Task>[]
        {
            db => db.Set<SiteMember>().Where(m => m.AccountId == world.Shared).ExecuteDeleteAsync(Timeout),
            db => db.Set<SiteMember>().Where(m => m.AccountId == world.Shared)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.Status, SiteMemberStatus.Suspended), Timeout),
        })
        {
            await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, () =>
                TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.B.Id, async () =>
                {
                    await using var scope = _harness.Scope();
                    var error = await Assert.ThrowsAsync<SqlException>(() => write(scope.ServiceProvider.GetRequiredService<PantryPressDbContext>()));
                    Assert.Equal(SiteMemberConfiguration.KeepAnOwnerErrorNumber, error.Number);
                }));
        }
    }

    // Every way of not reaching a site answers like a site that never existed: a stranger, a member of the look-alike
    // next door, a member suspended there, someone suspended here though active next door, and anyone at all once the
    // site is deleted - in either case of slug. Only a member already on the site learns that their role falls short.
    [Fact]
    public async Task Nothing_but_membership_tells_a_site_apart_from_no_site()
    {
        var world = await _harness.SeedAsync();
        var never = $"never-{Guid.NewGuid():N}";

        foreach (var (account, site, route) in new[]
        {
            (world.Stranger, world.B, "author"),
            (world.Stranger, world.B, "owner"),
            (world.A.Owner, world.B, "author"),
            (world.A.Owner, world.B, "owner"),
            (world.B.Suspended, world.B, "author"),
            (world.SuspendedOnA, world.A, "author"),
        })
        {
            foreach (var slug in new[] { site.Slug, site.Slug.ToUpperInvariant() })
            {
                using var client = _harness.ClientFor(account);
                using var real = await client.GetAsync(SitePolicyProbeController.PathFor(slug, route), Timeout);
                using var none = await client.GetAsync(SitePolicyProbeController.PathFor(never, route), Timeout);
                await TwoSiteChecks.AssertIndistinguishableAsync(real, none, HttpStatusCode.NotFound, SiteErrors.SiteNotFound.Code);
            }
        }

        using (var author = _harness.ClientFor(world.B.Author))
        using (var tooLow = await author.GetAsync(SitePolicyProbeController.PathFor(world.B.Slug, "owner"), Timeout))
        {
            Assert.Equal(HttpStatusCode.Forbidden, tooLow.StatusCode);
            Assert.Equal(SiteErrors.SiteRoleRequired.Code, await tooLow.CodeAsync());
        }

        using (var suspendedOnA = _harness.ClientFor(world.SuspendedOnA))
        using (var onB = await suspendedOnA.GetAsync(SitePolicyProbeController.PathFor(world.B.Slug, "editor"), Timeout))
        {
            Assert.Equal(HttpStatusCode.NoContent, onB.StatusCode);
        }

        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, () =>
            _harness.ChangeAsync(db => db.Set<Site>().Single(s => s.Id == world.B.Id).Status = SiteStatus.Deleted));

        using var owner = _harness.ClientFor(world.B.Owner);
        using var deleted = await owner.GetAsync(SitePolicyProbeController.PathFor(world.B.Slug, "author"), Timeout);
        using var nowhere = await owner.GetAsync(SitePolicyProbeController.PathFor(never, "author"), Timeout);
        await TwoSiteChecks.AssertIndistinguishableAsync(deleted, nowhere, HttpStatusCode.NotFound, SiteErrors.SiteNotFound.Code);

        using var neighbour = _harness.ClientFor(world.A.Owner);
        using var stillThere = await neighbour.GetAsync(SitePolicyProbeController.PathFor(world.A.Slug, "owner"), Timeout);
        Assert.Equal(HttpStatusCode.NoContent, stillThere.StatusCode);
    }

    // PP2.9: each site answers on its own subdomain, the two one letter apart. B cannot take A's host - as a custom domain,
    // waiting or not, or in another spelling of it - and A's rows are untouched by the attempt.
    [Fact]
    public async Task Neither_site_can_take_the_others_host()
    {
        var world = await _harness.SeedAsync();
        Assert.Equal([(world.A.Host, true)], (await HostsAsync(world.A.Id)).Select(h => (h.Host, h.IsCanonical)));
        Assert.Equal([(world.B.Host, true)], (await HostsAsync(world.B.Id)).Select(h => (h.Host, h.IsCanonical)));

        foreach (var status in new[] { SiteHostStatus.Pending, SiteHostStatus.Active })
        {
            await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, async () =>
            {
                var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
                    _harness.ChangeAsync(db => db.Add(Host(world.B.Id, world.A.Host, SiteHostKind.Custom, status))));
                Assert.Equal(UniqueIndexViolation, Assert.IsType<SqlException>(error.InnerException).Number);
            });
        }

        // Upper-case or fully qualified, it would be A's host once normalized, so the schema refuses either spelling.
        foreach (var spelling in new[] { world.A.Host.ToUpperInvariant(), world.A.Host + "." })
        {
            await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, async () =>
            {
                var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
                    _harness.ChangeAsync(db => db.Add(Host(world.B.Id, spelling, SiteHostKind.Custom, SiteHostStatus.Pending))));
                Assert.Equal(CheckConstraintViolation, Assert.IsType<SqlException>(error.InnerException).Number);
            });
        }

        Assert.Equal([world.B.Host], (await HostsAsync(world.B.Id)).Select(h => h.Host));
    }

    // B's canonical host moves to a custom domain and back, and B's extra host comes and goes; A's rows never change.
    [Fact]
    public async Task One_sites_host_changes_leave_the_others_alone()
    {
        var world = await _harness.SeedAsync();
        var custom = Host(world.B.Id, $"{world.B.Slug}.example.com", SiteHostKind.Custom, SiteHostStatus.Active);
        var subdomain = (await HostsAsync(world.B.Id)).Single().Id;

        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, async () =>
        {
            await _harness.ChangeAsync(db => db.Add(custom));
            await MoveCanonicalAsync(world.B.Id, custom.Id);
            await MoveCanonicalAsync(world.B.Id, subdomain);
            await _harness.ChangeAsync(db => db.Remove(db.Set<SiteHost>().Single(h => h.Id == custom.Id)));
        });

        Assert.Equal([(world.B.Host, true)], (await HostsAsync(world.B.Id)).Select(h => (h.Host, h.IsCanonical)));
    }

    // One statement over both sites' hosts: clearing both canonical hosts, removing every host of both, or moving a host of
    // A's to B. Each is refused whole, so neither site's rows change. The host moved is not A's canonical one, which B's
    // own canonical host would refuse first.
    [Fact]
    public async Task One_write_across_both_sites_hosts_is_refused_whole()
    {
        var world = await _harness.SeedAsync();
        var custom = Host(world.A.Id, $"{world.A.Slug}.example.com", SiteHostKind.Custom, SiteHostStatus.Active);
        await _harness.ChangeAsync(db => db.Add(custom));

        foreach (var (write, number) in new (Func<PantryPressDbContext, Task> Write, int Number)[]
        {
            (db => db.Set<SiteHost>().Where(h => h.SiteId == world.A.Id || h.SiteId == world.B.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(h => h.IsCanonical, false), Timeout), SiteHostConfiguration.KeepACanonicalErrorNumber),
            (db => db.Set<SiteHost>().Where(h => h.SiteId == world.A.Id || h.SiteId == world.B.Id)
                .ExecuteDeleteAsync(Timeout), SiteHostConfiguration.KeepACanonicalErrorNumber),
            (db => db.Set<SiteHost>().Where(h => h.Host == custom.Host)
                .ExecuteUpdateAsync(set => set.SetProperty(h => h.SiteId, world.B.Id), Timeout), SiteHostConfiguration.FixedHostErrorNumber),
        })
        {
            await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, () =>
                TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.B.Id, async () =>
                {
                    await using var scope = _harness.Scope();
                    var error = await Assert.ThrowsAsync<SqlException>(() => write(scope.ServiceProvider.GetRequiredService<PantryPressDbContext>()));
                    Assert.Equal(number, error.Number);
                }));
        }
    }

    // Each seeded account on each site, and the role it should find there: both sites' members both ways, and the three
    // accounts that cross between them.
    private static IEnumerable<(AccountId Account, SeededSite Site, SiteRole? Role)> Standings(TwoSiteWorld world)
    {
        foreach (var (home, other) in new[] { (world.A, world.B), (world.B, world.A) })
        {
            yield return (home.Owner, home, SiteRole.Owner);
            yield return (home.Editor, home, SiteRole.Editor);
            yield return (home.Author, home, SiteRole.Author);
            yield return (home.Suspended, home, null);
            foreach (var member in new[] { home.Owner, home.Editor, home.Author, home.Suspended })
            {
                yield return (member, other, null);
            }
        }

        yield return (world.Shared, world.A, SiteRole.Author);
        yield return (world.Shared, world.B, SiteRole.Owner);
        yield return (world.SuspendedOnA, world.A, null);
        yield return (world.SuspendedOnA, world.B, SiteRole.Editor);
        yield return (world.Stranger, world.A, null);
        yield return (world.Stranger, world.B, null);
    }

    private async Task<List<AccountId>> OwnersAsync(SiteId site)
    {
        await using var scope = _harness.Scope();
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<SiteMember>()
            .Where(m => m.SiteId == site && m.Role == SiteRole.Owner)
            .Select(m => m.AccountId)
            .ToListAsync(Timeout);
    }

    private async Task<List<SiteHost>> HostsAsync(SiteId site)
    {
        await using var scope = _harness.Scope();
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<SiteHost>()
            .AsNoTracking()
            .Where(h => h.SiteId == site)
            .ToListAsync(Timeout);
    }

    // In one statement, as the canonical host must move (SiteHostConfiguration.GuardTriggerName).
    private async Task MoveCanonicalAsync(SiteId site, SiteHostId to)
    {
        await using var scope = _harness.Scope();
        await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<SiteHost>()
            .Where(h => h.SiteId == site && (h.IsCanonical || h.Id == to))
            .ExecuteUpdateAsync(set => set.SetProperty(h => h.IsCanonical, h => h.Id == to), Timeout);
    }

    private static SiteHost Host(SiteId site, string host, SiteHostKind kind, SiteHostStatus status) => new()
    {
        Id = new SiteHostId(Guid.NewGuid()),
        SiteId = site,
        Host = host,
        Kind = kind,
        Status = status,
        CreatedAt = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero),
    };

    private static ISiteMembershipFacade Memberships(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ISiteMembershipFacade>();

    private static ISiteContext Context(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<ISiteContext>();
}
