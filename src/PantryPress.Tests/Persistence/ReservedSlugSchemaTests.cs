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

// Inside the namespace, so the type wins over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;

// PP2.8: the reserved list against the schema the migration service applies. The seed is the list in code; beneath the slug
// policy, the database refuses a reserved slug on any site write, and refuses reserving a slug a site already holds.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class ReservedSlugSchemaTests(MigratedDatabase database) : IAsyncLifetime
{
    private const int SlugReserved = SiteConfiguration.SlugReservedErrorNumber;
    private const int SlugHeld = ReservedSlugConfiguration.NotHeldErrorNumber;
    private const int CheckConstraintViolation = 547;

    private const string NotHeldTrigger =
        "TRIGGER [" + ReservedSlugConfiguration.NotHeldTriggerName + "] ON [" + ReservedSlugConfiguration.TableName + "]";

    private const string DisableNotHeld = "DISABLE " + NotHeldTrigger;
    private const string EnableNotHeld = "ENABLE " + NotHeldTrigger;

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

    [Fact]
    public async Task The_migrated_list_is_the_list_in_code()
    {
        await using var scope = _services.CreateAsyncScope();
        var stored = await Db(scope).Set<ReservedSlug>().AsNoTracking().ToListAsync(Timeout);

        Assert.Equal(
            ReservedSlugs.All.OrderBy(e => e.Slug, StringComparer.Ordinal),
            stored.Select(r => new ReservedSlugEntry(r.Slug, r.Reason, r.Since)).OrderBy(e => e.Slug, StringComparer.Ordinal));
    }

    // Every reason, and app above all: a write that skips the policy still cannot take one.
    [Theory]
    [InlineData("app")]
    [InlineData("dev")]
    [InlineData("www")]
    [InlineData("autodiscover")]
    [InlineData("pantrypress")]
    [InlineData("pricing")]
    public async Task A_new_site_cannot_take_a_reserved_slug(string slug)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(NewSite(slug)));

        Assert.Equal(SlugReserved, Assert.IsType<SqlException>(error.InnerException).Number);
    }

    [Fact]
    public async Task A_site_cannot_be_renamed_to_a_reserved_slug()
    {
        var site = NewSite(UniqueSlug("rename"));
        await AddAsync(site);

        await using (var scope = _services.CreateAsyncScope())
        {
            var db = Db(scope);
            var stored = await db.Set<Site>().SingleAsync(s => s.Id == site.Id, Timeout);
            stored.Slug = "app";

            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Timeout));
            Assert.Equal(SlugReserved, Assert.IsType<SqlException>(error.InnerException).Number);
        }

        Assert.Equal(site.Slug, (await FindAsync(site.Id)).Slug);
    }

    // A bulk update skips the save, and the policy with it; the trigger still sees every row, and refuses them all.
    [Fact]
    public async Task A_bulk_rename_to_a_reserved_slug_is_refused()
    {
        var site = NewSite(UniqueSlug("bulk"));
        await AddAsync(site);

        await using var scope = _services.CreateAsyncScope();
        var error = await Assert.ThrowsAsync<SqlException>(() => Db(scope).Set<Site>()
            .Where(s => s.Id == site.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Slug, "status"), Timeout));

        Assert.Equal(SlugReserved, error.Number);
        Assert.Equal(site.Slug, (await FindAsync(site.Id)).Slug);
    }

    // Grandfathering, which only an operator with the trigger off could arrange: a site already holding a slug that is now
    // reserved keeps working, and only a change of slug is checked. Inside a transaction that is rolled back.
    [Fact]
    public async Task A_site_whose_slug_does_not_change_is_let_through()
    {
        var site = NewSite(UniqueSlug("kept"));

        await using var scope = _services.CreateAsyncScope();
        var db = Db(scope);
        await using var transaction = await db.Database.BeginTransactionAsync(Timeout);
        db.Add(site);
        await db.SaveChangesAsync(Timeout);
        await db.Database.ExecuteSqlRawAsync(DisableNotHeld, Timeout);
        db.Add(new ReservedSlug { Slug = site.Slug, Reason = ReservedSlugReason.Product, Since = ReservedSlugs.Version });
        await db.SaveChangesAsync(Timeout);
        await db.Database.ExecuteSqlRawAsync(EnableNotHeld, Timeout);

        site.Name = "Still here";
        await db.SaveChangesAsync(Timeout);

        await transaction.RollbackAsync(Timeout);
    }

    // A list edit never takes an address from a site: the migration that tries fails. A deleted site still holds its slug.
    [Theory]
    [InlineData(SiteStatus.Live)]
    [InlineData(SiteStatus.Deleted)]
    public async Task A_slug_a_site_holds_cannot_be_reserved(SiteStatus status)
    {
        var site = NewSite(UniqueSlug("held"));
        site.Status = status;
        await AddAsync(site);

        await using var scope = _services.CreateAsyncScope();
        var db = Db(scope);
        db.Add(new ReservedSlug { Slug = site.Slug, Reason = ReservedSlugReason.Product, Since = ReservedSlugs.Version });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Timeout));
        var sql = Assert.IsType<SqlException>(error.InnerException);
        Assert.Equal(SlugHeld, sql.Number);
        Assert.Contains($"'{site.Slug}'", sql.Message, StringComparison.Ordinal);
    }

    // The lock hint's proof. The database reads committed snapshots (PP2.3), so without it a reservation would read past a
    // site claiming the same slug in a transaction not yet committed, and both would commit. With it, the reservation waits
    // for the claim and then fails.
    [Fact]
    public async Task A_reservation_waits_for_a_site_claiming_the_slug_and_then_fails()
    {
        var slug = UniqueSlug("race");

        await using var claimScope = _services.CreateAsyncScope();
        var claim = Db(claimScope);
        await using var claimTransaction = await claim.Database.BeginTransactionAsync(Timeout);
        claim.Add(NewSite(slug));
        await claim.SaveChangesAsync(Timeout);

        await using var reserveScope = _services.CreateAsyncScope();
        var reserve = Db(reserveScope);
        reserve.Add(new ReservedSlug { Slug = slug, Reason = ReservedSlugReason.Product, Since = ReservedSlugs.Version });
        var reserving = reserve.SaveChangesAsync(Timeout);

        await Task.Delay(TimeSpan.FromSeconds(1), Timeout);
        Assert.False(reserving.IsCompleted, "The reservation read past a site whose claim had not committed.");

        await claimTransaction.CommitAsync(Timeout);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => reserving);
        Assert.Equal(SlugHeld, Assert.IsType<SqlException>(error.InnerException).Number);
    }

    // The control for the test above: a slug nobody holds can be reserved, and then no site can take it.
    [Fact]
    public async Task A_slug_nobody_holds_can_be_reserved_and_then_not_taken()
    {
        var slug = UniqueSlug("free");

        await using var scope = _services.CreateAsyncScope();
        var db = Db(scope);
        await using var transaction = await db.Database.BeginTransactionAsync(Timeout);
        db.Add(new ReservedSlug { Slug = slug, Reason = ReservedSlugReason.Product, Since = ReservedSlugs.Version });
        await db.SaveChangesAsync(Timeout);

        db.Add(NewSite(slug));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Timeout));
        Assert.Equal(SlugReserved, Assert.IsType<SqlException>(error.InnerException).Number);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public async Task A_reason_outside_the_list_is_refused(int reason)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = Db(scope);
        db.Add(new ReservedSlug { Slug = UniqueSlug("why"), Reason = (ReservedSlugReason)reason, Since = ReservedSlugs.Version });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Timeout));
        Assert.Equal(CheckConstraintViolation, Assert.IsType<SqlException>(error.InnerException).Number);
        Assert.Contains(ReservedSlugConfiguration.ReasonConstraintName, error.InnerException!.Message, StringComparison.Ordinal);
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
        CreatedAt = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero),
    };

    // Lower-case hex keeps every slug inside the shape and unique to this run, since the database is shared.
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
}
