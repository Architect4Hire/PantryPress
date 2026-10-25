using Aspire.Hosting.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Persistence;

// Inside the namespace, so the type wins over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;

// PP2.2: the site table (SITE-001) against the schema the migration service applies. Two sites that look alike live
// side by side and stay apart, a slug is the platform's one at a time, a save from a stale read is refused, and the
// slug-shape and status checks hold under PP2.8's slug policy even for a write that skips it.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class SiteSchemaTests(MigratedDatabase database) : IAsyncLifetime
{
    private const int UniqueIndexViolation = 2601;
    private const int CheckConstraintViolation = 547;
    private const int StringTruncation = 2628;

    private ServiceProvider _services = null!;

    private static CancellationToken Timeout => CancellationToken.None;

    public async Task InitializeAsync()
    {
        var connectionString = await database.App.GetConnectionStringAsync(MigrationServiceExtensions.ConnectionName, Timeout)
            ?? throw new InvalidOperationException("The test AppHost has no pantrypress connection string.");

        var services = new ServiceCollection();
        services.AddAccountsIdentitySchema();
        services.AddDbContext<PantryPressDbContext>(options => options.UseSqlServer(
            connectionString,
            MigrationServiceExtensions.UseMigrationsAssembly));
        _services = services.BuildServiceProvider();
    }

    public Task DisposeAsync() => _services.DisposeAsync().AsTask();

    [Fact]
    public async Task Two_look_alike_sites_are_kept_apart()
    {
        var a = NewSite(UniqueSlug("a"));
        var b = NewSite(UniqueSlug("b"));
        await AddAsync(a, b);
        var aVersion = (await FindAsync(a.Id)).Version;

        await using (var scope = _services.CreateAsyncScope())
        {
            var db = Db(scope);
            var site = await db.Set<Site>().SingleAsync(s => s.Id == b.Id, Timeout);
            site.Tagline = "Only B changed.";
            await db.SaveChangesAsync(Timeout);
        }

        var storedA = await FindAsync(a.Id);
        var storedB = await FindAsync(b.Id);
        Assert.Equal("Weeknight dinners", storedA.Tagline);
        Assert.Equal(aVersion, storedA.Version);
        Assert.Equal("Only B changed.", storedB.Tagline);
    }

    [Fact]
    public async Task A_second_site_cannot_take_a_slug_already_held()
    {
        var slug = UniqueSlug("held");
        await AddAsync(NewSite(slug));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(NewSite(slug)));

        Assert.Equal(UniqueIndexViolation, Assert.IsType<SqlException>(error.InnerException).Number);
        Assert.Contains(SiteConfiguration.SlugIndexName, error.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_save_from_a_stale_read_is_refused()
    {
        var site = NewSite(UniqueSlug("race"));
        await AddAsync(site);

        await using var first = _services.CreateAsyncScope();
        await using var second = _services.CreateAsyncScope();
        var fresh = await Db(first).Set<Site>().SingleAsync(s => s.Id == site.Id, Timeout);
        var stale = await Db(second).Set<Site>().SingleAsync(s => s.Id == site.Id, Timeout);

        fresh.Name = "First writer";
        await Db(first).SaveChangesAsync(Timeout);
        stale.Name = "Second writer";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Db(second).SaveChangesAsync(Timeout));
        Assert.Equal("First writer", (await FindAsync(site.Id)).Name);
    }

    // The policy that decides a slug is PP2.8's; this is the backstop under it. Upper-case is refused outright, which is
    // what lets a byte-for-byte unique index stand in for a case-insensitive one. The Cyrillic look-alike is refused only
    // because the client's code page turns it into '?' before the server sees it, which is why slugs are checked for ASCII
    // in .NET before any lookup (tenancy.md, "Tenant root").
    [Theory]
    [InlineData("Pie")]
    [InlineData("ab")]
    [InlineData("pie crust")]
    [InlineData("pie_crust")]
    [InlineData("pie.crust")]
    [InlineData("café")]
    [InlineData("pіe")]
    [InlineData("pie ")]
    [InlineData("")]
    public async Task A_slug_outside_the_shape_is_refused(string slug)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(NewSite(slug)));

        Assert.Equal(CheckConstraintViolation, Assert.IsType<SqlException>(error.InnerException).Number);
        Assert.Contains(SiteConfiguration.SlugShapeConstraintName, error.InnerException!.Message, StringComparison.Ordinal);
    }

    // The column is the limit's width, so the server refuses an over-long slug before the check sees it.
    [Fact]
    public async Task A_slug_over_the_limit_is_refused_rather_than_cut_short()
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(NewSite(new string('a', SiteLimits.MaxSlugLength + 1))));

        Assert.Equal(StringTruncation, Assert.IsType<SqlException>(error.InnerException).Number);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public async Task A_status_outside_the_list_is_refused(int status)
    {
        var site = NewSite(UniqueSlug("status"));
        site.Status = (SiteStatus)status;

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(site));

        Assert.Equal(CheckConstraintViolation, Assert.IsType<SqlException>(error.InnerException).Number);
        Assert.Contains(SiteConfiguration.StatusConstraintName, error.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_shortest_and_longest_slugs_are_accepted()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var shortest = suffix[..SiteLimits.MinSlugLength];
        var longest = ("x-" + suffix + suffix)[..SiteLimits.MaxSlugLength];

        await AddAsync(NewSite(shortest), NewSite(longest));

        Assert.Equal(2, await CountAsync(shortest, longest));
    }

    private static Site NewSite(string slug) => new()
    {
        Id = new SiteId(Guid.NewGuid()),
        Slug = slug,
        Name = "Sunday Supper",
        Tagline = "Weeknight dinners",
        Status = SiteStatus.Provisioning,
        TimeZone = "America/Chicago",
        Locale = "en-US",
        CreatedAt = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero),
    };

    // Lower-case hex keeps every slug inside the shape and unique to this run, since the database is shared. Prefixes
    // stay short enough to fit the 32 digits within the limit.
    private static string UniqueSlug(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static PantryPressDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();

    private async Task AddAsync(params Site[] sites)
    {
        await using var scope = _services.CreateAsyncScope();
        Db(scope).AddRange(sites);
        await Db(scope).SaveChangesAsync(Timeout);
    }

    private async Task<Site> FindAsync(SiteId id)
    {
        await using var scope = _services.CreateAsyncScope();
        return await Db(scope).Set<Site>().AsNoTracking().SingleAsync(s => s.Id == id, Timeout);
    }

    private async Task<int> CountAsync(params string[] slugs)
    {
        await using var scope = _services.CreateAsyncScope();
        return await Db(scope).Set<Site>().CountAsync(s => slugs.Contains(s.Slug), Timeout);
    }
}
