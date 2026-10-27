using Aspire.Hosting.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Persistence;

// Inside the namespace, so the types win over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;

// PP2.9: a site's hosts (SITE-005) against the schema the migration service applies. A host belongs to one site, is
// stored in one shape - the shape SiteHostNames gives it, which this holds even for a write that skips it - and never
// changes site or spelling; each site that is not deleted keeps exactly one canonical host, which moves in one statement.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class SiteHostSchemaTests(MigratedDatabase database) : IAsyncLifetime
{
    private const int UniqueIndexViolation = 2601;
    private const int ConstraintViolation = 547;
    private const int StringTruncation = 2628;
    private const int KeepACanonical = SiteHostConfiguration.KeepACanonicalErrorNumber;
    private const int FixedHost = SiteHostConfiguration.FixedHostErrorNumber;

    // Stands for a label no other test or run holds, since the database is shared.
    private const string Fresh = "{fresh}";

    private ServiceProvider _services = null!;

    private static CancellationToken Timeout => CancellationToken.None;

    public async Task InitializeAsync()
    {
        var connectionString = await database.App.GetConnectionStringAsync(MigrationServiceExtensions.ConnectionName, Timeout)
            ?? throw new InvalidOperationException("The test AppHost has no pantrypress connection string.");

        var services = new ServiceCollection();
        services.AddAccountsIdentitySchema();
        services.AddPantryPressDbContext((_, options) => options.UseSqlServer(
            connectionString,
            MigrationServiceExtensions.UseMigrationsAssembly));
        _services = services.BuildServiceProvider();
    }

    public Task DisposeAsync() => _services.DisposeAsync().AsTask();

    // Each value as it would be written: stored exactly when SiteHostNames gives it back unchanged, and refused otherwise,
    // so the .NET rule and the schema's backstop agree. Only ASCII here: anything else is refused in .NET before a lookup
    // or a write, because the client's code page conversion could turn a look-alike into a real letter before the server
    // saw it (SiteHostNamesTests).
    public static TheoryData<string, bool> Shapes => new()
    {
        { $"{Fresh}.pantrypress.app", true },
        { $"{Fresh}.localhost", true },
        { $"www.{Fresh}.co.uk", true },
        { $"xn--{Fresh}.com", true },
        { $"{Fresh}.example.c0m", true },
        { $"{Fresh}{new string('a', 30)}.example.com", true }, // a 63-character label
        { $"{Fresh}.{new string('a', 63)}.{new string('a', 63)}.{new string('a', 63)}.{new string('b', 27)}", true }, // 253 characters
        { $"{Fresh}.Example.com", false },
        { $"{Fresh}.example.com.", false },
        { $"{Fresh}.example.com:443", false },
        { $"{Fresh}.example.com ", false },
        { $".{Fresh}.example.com", false },
        { $"{Fresh}..example.com", false },
        { $"-{Fresh}.example.com", false },
        { $"{Fresh}-.example.com", false },
        { $"{Fresh}.-example.com", false },
        { $"{Fresh}_x.example.com", false },
        { $"{Fresh} x.example.com", false },
        { $"{Fresh}{new string('a', 31)}.example.com", false }, // a 64-character label
        { $"{Fresh}.example.123", false },
        { $"{Fresh}.0x1", false },
        { $"{Fresh}.1com", false },
        { "127.0.0.0x1", false },
        { Fresh, false },
        { "localhost", false },
        { "127.0.0.1", false },
        { "127.1", false },
        { "0x7f.1", false },
        { "[::1]", false },
        { "::1", false },
        { "a.", false },
        { string.Empty, false },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task The_schema_stores_exactly_the_shape_SiteHostNames_gives(string template, bool stored)
    {
        var host = template.Replace(Fresh, FreshLabel(), StringComparison.Ordinal);
        Assert.Equal(stored, SiteHostNames.TryNormalize(host, out var normalized) && normalized == host);

        var site = await NewSiteAsync();
        var write = AddAsync(Host(site, host, SiteHostKind.Custom));
        if (stored)
        {
            await write;
            Assert.Equal(host, (await HostsAsync(site)).Single(h => h.Kind == SiteHostKind.Custom).Host);
        }
        else
        {
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => write);
            var number = Assert.IsType<SqlException>(error.InnerException).Number;
            Assert.True(number is ConstraintViolation or StringTruncation, $"Refused with {number}.");
            if (number == ConstraintViolation)
            {
                Assert.Contains(SiteHostConfiguration.HostShapeConstraintName, error.InnerException!.Message, StringComparison.Ordinal);
            }
        }
    }

    // The column is the limit's width, so the server refuses an over-long host before the check sees it.
    [Fact]
    public async Task A_host_over_the_limit_is_refused_rather_than_cut_short()
    {
        var site = await NewSiteAsync();
        var host = $"{FreshLabel()}.{new string('a', 63)}.{new string('a', 63)}.{new string('a', 63)}.{new string('b', 28)}";
        Assert.Equal(SiteLimits.MaxHostLength + 1, host.Length);

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(Host(site, host, SiteHostKind.Custom)));

        Assert.Equal(StringTruncation, Assert.IsType<SqlException>(error.InnerException).Number);
    }

    // DOM-004: a host attaches to one site platform-wide, whatever its kind or status.
    [Fact]
    public async Task A_second_site_cannot_take_a_host_already_held()
    {
        var a = await NewSiteAsync();
        var b = await NewSiteAsync();
        var held = $"{FreshLabel()}.example.com";
        await AddAsync(Host(a, held, SiteHostKind.Custom, status: SiteHostStatus.Pending));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(Host(b, held, SiteHostKind.Custom)));

        Assert.Equal(UniqueIndexViolation, Assert.IsType<SqlException>(error.InnerException).Number);
        Assert.Contains(SiteHostConfiguration.HostIndexName, error.InnerException!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(await HostsAsync(b), h => h.Host == held);
    }

    [Fact]
    public async Task A_site_has_one_canonical_host_at_a_time()
    {
        var site = await NewSiteAsync();

        var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
            AddAsync(Host(site, $"{FreshLabel()}.example.com", SiteHostKind.Custom, canonical: true)));

        Assert.Equal(UniqueIndexViolation, Assert.IsType<SqlException>(error.InnerException).Number);
        Assert.Contains(SiteHostConfiguration.CanonicalIndexName, error.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_site_cannot_lose_its_canonical_host()
    {
        var site = await NewSiteAsync();
        var other = Host(site, $"{FreshLabel()}.example.com", SiteHostKind.Custom);
        await AddAsync(other);
        var canonical = (await HostsAsync(site)).Single(h => h.IsCanonical).Id;

        await AssertRefusedAsync(KeepACanonical, db => Tracked(db, canonical).IsCanonical = false);
        await AssertRefusedAsync(KeepACanonical, db => db.Remove(Tracked(db, canonical)));
        await AssertBulkRefusedAsync(KeepACanonical, db => db.Set<SiteHost>().Where(h => h.SiteId == site).ExecuteDeleteAsync(Timeout));

        // Hosts that are not canonical come and go freely.
        await ChangeAsync(db => db.Remove(Tracked(db, other.Id)));
        Assert.Equal([canonical], (await HostsAsync(site)).Select(h => h.Id));
    }

    // Moving the canonical host (DOM-003) is one statement: the filtered index is checked at its end, and the trigger after
    // it, so the site has one canonical host before and after and never none or two.
    [Fact]
    public async Task The_canonical_host_moves_in_one_statement()
    {
        var site = await NewSiteAsync();
        var custom = Host(site, $"{FreshLabel()}.example.com", SiteHostKind.Custom);
        await AddAsync(custom);
        var subdomain = (await HostsAsync(site)).Single(h => h.IsCanonical).Id;

        await MoveCanonicalAsync(site, custom.Id);
        Assert.Equal([custom.Id], (await HostsAsync(site)).Where(h => h.IsCanonical).Select(h => h.Id));

        await MoveCanonicalAsync(site, subdomain);
        Assert.Equal([subdomain], (await HostsAsync(site)).Where(h => h.IsCanonical).Select(h => h.Id));
    }

    // Two statements cannot do it, in either order and whatever order EF Core sends them in: one of the two guards always
    // sees the step between.
    [Fact]
    public async Task The_canonical_host_cannot_move_in_two_statements()
    {
        var site = await NewSiteAsync();
        var custom = Host(site, $"{FreshLabel()}.example.com", SiteHostKind.Custom);
        await AddAsync(custom);
        var subdomain = (await HostsAsync(site)).Single(h => h.IsCanonical).Id;

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => ChangeAsync(db =>
        {
            Tracked(db, subdomain).IsCanonical = false;
            Tracked(db, custom.Id).IsCanonical = true;
        }));

        Assert.True(Assert.IsType<SqlException>(error.InnerException).Number is KeepACanonical or UniqueIndexViolation);
        Assert.Equal([subdomain], (await HostsAsync(site)).Where(h => h.IsCanonical).Select(h => h.Id));
    }

    [Fact]
    public async Task A_deleted_site_can_lose_its_hosts()
    {
        var site = await NewSiteAsync();
        await ChangeAsync(db => db.Set<Site>().Single(s => s.Id == site).Status = SiteStatus.Deleted);

        await ChangeAsync(db => db.RemoveRange(db.Set<SiteHost>().Where(h => h.SiteId == site)));

        Assert.Empty(await HostsAsync(site));
    }

    // A host changes hands only by being removed and added again, so a row never moves to another site or is respelled.
    [Fact]
    public async Task A_host_never_changes_site_or_spelling()
    {
        var a = await NewSiteAsync();
        var b = await NewSiteAsync();
        var custom = Host(a, $"{FreshLabel()}.example.com", SiteHostKind.Custom);
        await AddAsync(custom);

        await AssertBulkRefusedAsync(FixedHost, db => db.Set<SiteHost>()
            .Where(h => h.Id == custom.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(h => h.SiteId, b), Timeout));
        await AssertBulkRefusedAsync(FixedHost, db => db.Set<SiteHost>()
            .Where(h => h.Id == custom.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(h => h.Host, $"{FreshLabel()}.example.com"), Timeout));
        await AssertBulkRefusedAsync(FixedHost, db => db.Database.ExecuteSqlAsync(
            $"UPDATE [SiteHosts] SET [Id] = NEWID() WHERE [Id] = {custom.Id.Value}",
            Timeout));

        Assert.Equal(custom.Host, (await HostsAsync(a)).Single(h => h.Id == custom.Id).Host);
        Assert.Single(await HostsAsync(b));
    }

    // A subdomain is the platform's own address and a custom domain one that is verified, so neither becomes the other.
    [Theory]
    [InlineData(SiteHostKind.Custom, SiteHostKind.Subdomain)]
    [InlineData(SiteHostKind.WwwVariant, SiteHostKind.Subdomain)]
    [InlineData(SiteHostKind.Subdomain, SiteHostKind.Custom)]
    public async Task A_host_never_becomes_or_stops_being_a_subdomain(SiteHostKind from, SiteHostKind to)
    {
        var site = await NewSiteAsync();
        var host = Host(site, $"{FreshLabel()}.example.com", from);
        await AddAsync(host);

        await AssertRefusedAsync(FixedHost, db => Tracked(db, host.Id).Kind = to);
        Assert.Equal(from, (await HostsAsync(site)).Single(h => h.Id == host.Id).Kind);
    }

    // Whether a custom domain is the apex or its www variant, and whether it is waiting, can change; its site, spelling,
    // and being a subdomain or not are fixed.
    [Fact]
    public async Task A_hosts_kind_and_status_can_change()
    {
        var site = await NewSiteAsync();
        var custom = Host(site, $"{FreshLabel()}.example.com", SiteHostKind.Custom, status: SiteHostStatus.Pending);
        await AddAsync(custom);

        await ChangeAsync(db =>
        {
            var host = Tracked(db, custom.Id);
            host.Kind = SiteHostKind.WwwVariant;
            host.Status = SiteHostStatus.Active;
        });

        var stored = (await HostsAsync(site)).Single(h => h.Id == custom.Id);
        Assert.Equal((SiteHostKind.WwwVariant, SiteHostStatus.Active), (stored.Kind, stored.Status));
    }

    [Theory]
    [InlineData(SiteHostKind.Custom, SiteHostStatus.Pending)]
    [InlineData(SiteHostKind.WwwVariant, SiteHostStatus.Active)]
    public async Task A_canonical_host_is_active_and_never_a_www_variant(SiteHostKind kind, SiteHostStatus status)
    {
        var site = await NewSiteAsync(withSubdomain: false);

        var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
            AddAsync(Host(site, $"{FreshLabel()}.example.com", kind, canonical: true, status: status)));

        Assert.Equal(ConstraintViolation, Assert.IsType<SqlException>(error.InnerException).Number);
        Assert.Contains(SiteHostConfiguration.CanonicalConstraintName, error.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_canonical_host_cannot_be_put_on_hold()
    {
        var site = await NewSiteAsync();
        var canonical = (await HostsAsync(site)).Single(h => h.IsCanonical).Id;

        await AssertRefusedAsync(ConstraintViolation, db => Tracked(db, canonical).Status = SiteHostStatus.Pending);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(3, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 2)]
    public async Task A_kind_or_status_outside_the_list_is_refused(int kind, int status)
    {
        var site = await NewSiteAsync();

        var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
            AddAsync(Host(site, $"{FreshLabel()}.example.com", (SiteHostKind)kind, status: (SiteHostStatus)status)));

        Assert.Equal(ConstraintViolation, Assert.IsType<SqlException>(error.InnerException).Number);
        Assert.Contains(
            kind is < 0 or > 2 ? SiteHostConfiguration.KindConstraintName : SiteHostConfiguration.StatusConstraintName,
            error.InnerException!.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_needs_a_real_site()
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
            AddAsync(Host(new SiteId(Guid.NewGuid()), $"{FreshLabel()}.example.com", SiteHostKind.Custom)));

        Assert.Equal(ConstraintViolation, Assert.IsType<SqlException>(error.InnerException).Number);
    }

    [Fact]
    public async Task A_save_from_a_stale_read_is_refused()
    {
        var site = await NewSiteAsync();
        var custom = Host(site, $"{FreshLabel()}.example.com", SiteHostKind.Custom, status: SiteHostStatus.Pending);
        await AddAsync(custom);

        await using var first = _services.CreateAsyncScope();
        await using var second = _services.CreateAsyncScope();
        var fresh = Tracked(Db(first), custom.Id);
        var stale = Tracked(Db(second), custom.Id);

        fresh.Status = SiteHostStatus.Active;
        await Db(first).SaveChangesAsync(Timeout);
        stale.Kind = SiteHostKind.WwwVariant;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Db(second).SaveChangesAsync(Timeout));
    }

    // Starts with a letter and holds a fresh id, so the hosts it makes are this test's alone.
    private static string FreshLabel() => $"h{Guid.NewGuid():N}";

    private static SiteHost Host(
        SiteId site,
        string host,
        SiteHostKind kind,
        bool canonical = false,
        SiteHostStatus status = SiteHostStatus.Active) => new()
        {
            Id = new SiteHostId(Guid.NewGuid()),
            SiteId = site,
            Host = host,
            Kind = kind,
            IsCanonical = canonical,
            Status = status,
            CreatedAt = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero),
        };

    private static PantryPressDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();

    private static SiteHost Tracked(PantryPressDbContext db, SiteHostId id) => db.Set<SiteHost>().Single(h => h.Id == id);

    // A site and its canonical subdomain are saved together, as creating a site will (PP2.10).
    private async Task<SiteId> NewSiteAsync(bool withSubdomain = true)
    {
        var slug = FreshLabel();
        var site = new Site
        {
            Id = new SiteId(Guid.NewGuid()),
            Slug = slug,
            Name = "Sunday Supper",
            Tagline = "Weeknight dinners",
            Status = SiteStatus.Live,
            TimeZone = "America/Chicago",
            Locale = "en-US",
            CreatedAt = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero),
        };

        await ChangeAsync(db =>
        {
            db.Add(site);
            if (withSubdomain)
            {
                db.Add(Host(site.Id, $"{slug}.pantrypress.app", SiteHostKind.Subdomain, canonical: true));
            }
        });
        return site.Id;
    }

    private Task MoveCanonicalAsync(SiteId site, SiteHostId to) => BulkAsync(db => db.Set<SiteHost>()
        .Where(h => h.SiteId == site && (h.IsCanonical || h.Id == to))
        .ExecuteUpdateAsync(set => set.SetProperty(h => h.IsCanonical, h => h.Id == to), Timeout));

    private Task AddAsync(SiteHost host) => ChangeAsync(db => db.Add(host));

    private async Task<List<SiteHost>> HostsAsync(SiteId site)
    {
        await using var scope = _services.CreateAsyncScope();
        return await Db(scope).Set<SiteHost>().AsNoTracking().Where(h => h.SiteId == site).ToListAsync(Timeout);
    }

    private async Task ChangeAsync(Action<PantryPressDbContext> change)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = Db(scope);
        change(db);
        await db.SaveChangesAsync(Timeout);
    }

    private async Task BulkAsync(Func<PantryPressDbContext, Task> change)
    {
        await using var scope = _services.CreateAsyncScope();
        await change(Db(scope));
    }

    private async Task AssertRefusedAsync(int number, Action<PantryPressDbContext> change)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => ChangeAsync(change));
        Assert.Equal(number, Assert.IsType<SqlException>(error.InnerException).Number);
    }

    private async Task AssertBulkRefusedAsync(int number, Func<PantryPressDbContext, Task> change)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => BulkAsync(change));
        Assert.Equal(number, error.Number);
    }
}
