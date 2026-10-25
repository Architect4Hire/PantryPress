using System.Data.Common;
using Aspire.Hosting.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Persistence;

// Inside the namespace, so the types win over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;

// PP2.3: membership (SITE-010) against the schema the migration service applies. The database keeps every site with an
// active owner - including against two owners stepping down at once, under the committed-snapshot reads Azure SQL uses
// and the migration service turns on everywhere -
// and the membership lookup the site policies ask answers each site's own role, and nothing for a site the caller cannot
// reach, through look-alike sites.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class SiteMemberSchemaTests(MigratedDatabase database) : IAsyncLifetime
{
    private const int KeepAnOwner = SiteMemberConfiguration.KeepAnOwnerErrorNumber;
    private const int PrimaryKeyViolation = 2627;
    private const int ForeignKeyViolation = 547;
    private const int Deadlocked = 1205;

    private ServiceProvider _services = null!;
    private string _connectionString = null!;

    private static CancellationToken Timeout => CancellationToken.None;

    private ISiteMembershipFacade Memberships(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<ISiteMembershipFacade>();

    public async Task InitializeAsync()
    {
        _connectionString = await database.App.GetConnectionStringAsync(MigrationServiceExtensions.ConnectionName, Timeout)
            ?? throw new InvalidOperationException("The test AppHost has no pantrypress connection string.");

        var services = new ServiceCollection();
        services.AddAccountsIdentitySchema();
        services.AddDbContext<PantryPressDbContext>(options => options.UseSqlServer(
            _connectionString,
            MigrationServiceExtensions.UseMigrationsAssembly));
        services.AddSites();
        _services = services.BuildServiceProvider();
    }

    public Task DisposeAsync() => _services.DisposeAsync().AsTask();

    [Fact]
    public async Task The_database_keeps_the_last_owner()
    {
        var (site, owner) = await SiteWithOwnerAsync();
        var editor = await AddMemberAsync(site, SiteRole.Editor);

        await AssertRefusedAsync(KeepAnOwner, db => Tracked(db, site, owner).Role = SiteRole.Editor);
        await AssertRefusedAsync(KeepAnOwner, db => Tracked(db, site, owner).Status = SiteMemberStatus.Suspended);
        await AssertRefusedAsync(KeepAnOwner, db => db.Remove(Tracked(db, site, owner)));

        // Members who are not the owner come and go freely.
        await ChangeAsync(db => db.Remove(Tracked(db, site, editor)));
        Assert.Equal(SiteRole.Owner, (await FindMemberAsync(site, owner))!.Role);
    }

    [Fact]
    public async Task A_bulk_change_cannot_skip_the_rule()
    {
        var (site, _) = await SiteWithOwnerAsync();

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var error = await Assert.ThrowsAsync<SqlException>(() =>
            db.Set<SiteMember>().Where(m => m.SiteId == site).ExecuteDeleteAsync(Timeout));

        Assert.Equal(KeepAnOwner, error.Number);
    }

    [Fact]
    public async Task An_owner_with_another_beside_them_can_step_down()
    {
        var (site, first) = await SiteWithOwnerAsync();
        var second = await AddMemberAsync(site, SiteRole.Owner);

        await ChangeAsync(db => Tracked(db, site, first).Role = SiteRole.Author);

        Assert.Equal(SiteRole.Author, (await FindMemberAsync(site, first))!.Role);
        Assert.Equal(SiteRole.Owner, (await FindMemberAsync(site, second))!.Role);
    }

    // Each owner's change alone leaves the other, so each passes a check made before its save. The trigger's locking read
    // makes the second wait for the first and then see it - which a plain read under committed snapshots would not.
    [Fact]
    public async Task Two_owners_stepping_down_at_once_leave_one()
    {
        var (site, first) = await SiteWithOwnerAsync();
        var second = await AddMemberAsync(site, SiteRole.Owner);

        await using var firstScope = _services.CreateAsyncScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        await using var transaction = await firstDb.Database.BeginTransactionAsync(Timeout);
        Tracked(firstDb, site, first).Role = SiteRole.Editor;
        await firstDb.SaveChangesAsync(Timeout);

        var secondSave = Task.Run(() => AssertRefusedAsync(KeepAnOwner, db => Tracked(db, site, second).Role = SiteRole.Editor));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(secondSave.IsCompleted, "The second owner's change did not wait for the first.");

        await transaction.CommitAsync(Timeout);
        await secondSave;

        Assert.Equal(SiteRole.Editor, (await FindMemberAsync(site, first))!.Role);
        Assert.Equal(SiteRole.Owner, (await FindMemberAsync(site, second))!.Role);
    }

    // Both changes issued at once, many times over: whichever way each pair interleaves - one waiting for the other, or a
    // deadlock that rolls one back - the site keeps exactly one owner.
    [Fact]
    public async Task Owners_stepping_down_at_the_same_instant_never_leave_none()
    {
        for (var round = 0; round < 10; round++)
        {
            var (site, first) = await SiteWithOwnerAsync();
            var second = await AddMemberAsync(site, SiteRole.Owner);

            var outcomes = await Task.WhenAll(
                Task.Run(() => TryChangeAsync(db => Tracked(db, site, first).Role = SiteRole.Editor)),
                Task.Run(() => TryChangeAsync(db => Tracked(db, site, second).Role = SiteRole.Editor)));

            Assert.Single(outcomes, o => o is null);
            Assert.Single(outcomes, o => o is KeepAnOwner or Deadlocked);
            Assert.Equal(1, await ActiveOwnersAsync(site));
        }
    }

    // Only losing an active owner is checked, so a site that somehow has none can still change its other members.
    [Fact]
    public async Task Changing_members_who_are_not_owners_is_never_refused()
    {
        var editor = NewAccount();
        var (site, _) = await NewSiteAsync("noown", (editor, SiteRole.Editor));

        await ChangeAsync(db => Tracked(db, site, editor).Role = SiteRole.Author);

        Assert.Equal(SiteRole.Author, (await FindMemberAsync(site, editor))!.Role);
    }

    [Fact]
    public async Task A_bulk_change_across_two_sites_is_refused_whole()
    {
        // One statement that would leave B with an owner and A with none: nothing of it stays, B's half included.
        var (a, aOwner) = await SiteWithOwnerAsync();
        var (b, bOwner) = await SiteWithOwnerAsync();
        await AddMemberAsync(b, SiteRole.Owner);

        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
            var error = await Assert.ThrowsAsync<SqlException>(() => db.Set<SiteMember>()
                .Where(m => m.SiteId == a || (m.SiteId == b && m.AccountId == bOwner))
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.Role, SiteRole.Editor), Timeout));
            Assert.Equal(KeepAnOwner, error.Number);
        }

        Assert.Equal(SiteRole.Owner, (await FindMemberAsync(a, aOwner))!.Role);
        Assert.Equal(2, await ActiveOwnersAsync(b));
    }

    [Fact]
    public async Task An_owner_cannot_be_moved_to_another_site()
    {
        var (a, owner) = await SiteWithOwnerAsync();
        var (b, _) = await SiteWithOwnerAsync();

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Set<SiteMember>()
            .Where(m => m.SiteId == a && m.AccountId == owner)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.SiteId, b), Timeout));

        Assert.Equal(KeepAnOwner, error.Number);
    }

    // The database reads the way Azure SQL does, which is what the trigger's locking read is for (PP2.3).
    [Fact]
    public async Task The_database_reads_committed_snapshots()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();

        var on = await db.Database
            .SqlQuery<bool>($"SELECT is_read_committed_snapshot_on AS [Value] FROM sys.databases WHERE database_id = DB_ID()")
            .SingleAsync(Timeout);

        Assert.True(on);
    }

    // The facade is scoped to a request: however many policies ask, each account and slug is looked up once, whatever
    // the slug's case, and another account's question is its own.
    [Fact]
    public async Task One_request_looks_each_membership_up_once()
    {
        var owner = NewAccount();
        var editor = NewAccount();
        var (_, slug) = await NewSiteAsync("once", (owner, SiteRole.Owner), (editor, SiteRole.Editor));
        var commands = new CountingCommands();
        await using var services = CountedServices(commands);
        await using var request = services.CreateAsyncScope();
        var memberships = Memberships(request);

        Assert.True((await memberships.AuthorizeAsync(owner, slug, SiteRole.Author, Timeout)).IsSuccess);
        Assert.True((await memberships.AuthorizeAsync(owner, slug.ToUpperInvariant(), SiteRole.Owner, Timeout)).IsSuccess);
        Assert.NotNull(await memberships.FindAsync(owner, slug, Timeout));
        var afterOwner = commands.Count;
        Assert.Equal(
            SiteErrors.SiteRoleRequired,
            (await memberships.AuthorizeAsync(editor, slug, SiteRole.Owner, Timeout)).Error);

        Assert.Equal(2, afterOwner); // the site by its slug, then the membership
        Assert.Equal(4, commands.Count);
    }

    [Fact]
    public async Task A_deleted_site_can_lose_its_members()
    {
        var (site, owner) = await SiteWithOwnerAsync();
        await ChangeAsync(db => db.Set<Site>().Single(s => s.Id == site).Status = SiteStatus.Deleted);

        await ChangeAsync(db => db.Remove(Tracked(db, site, owner)));

        Assert.Null(await FindMemberAsync(site, owner));
    }

    [Fact]
    public async Task An_account_is_a_member_of_a_site_once()
    {
        var (site, owner) = await SiteWithOwnerAsync();

        await AssertRefusedAsync(PrimaryKeyViolation, db => db.Add(Member(site, owner, SiteRole.Editor)));
    }

    [Fact]
    public async Task A_membership_needs_a_real_site() =>
        await AssertRefusedAsync(ForeignKeyViolation, db => db.Add(Member(new SiteId(Guid.NewGuid()), NewAccount(), SiteRole.Owner)));

    // Two sites that look alike, one account in both with different roles, and another in one of them only.
    [Fact]
    public async Task Each_site_answers_its_own_role_and_nothing_for_a_site_out_of_reach()
    {
        var both = NewAccount();
        var onlyA = NewAccount();
        var (a, aSlug) = await NewSiteAsync("a", (both, SiteRole.Owner), (onlyA, SiteRole.Author));
        var (b, bSlug) = await NewSiteAsync("b", (both, SiteRole.Editor), (NewAccount(), SiteRole.Owner));

        await using var scope = _services.CreateAsyncScope();
        var memberships = Memberships(scope);

        Assert.Equal(new SiteMembership(a, SiteRole.Owner), await memberships.FindAsync(both, aSlug, Timeout));
        Assert.Equal(new SiteMembership(b, SiteRole.Editor), await memberships.FindAsync(both, bSlug, Timeout));
        Assert.Equal(new SiteMembership(a, SiteRole.Author), await memberships.FindAsync(onlyA, aSlug, Timeout));
        Assert.Null(await memberships.FindAsync(onlyA, bSlug, Timeout));
        Assert.Null(await memberships.FindAsync(onlyA, UniqueSlug("none"), Timeout));
    }

    [Fact]
    public async Task A_slug_is_found_whatever_its_case_and_only_in_its_shape()
    {
        var account = NewAccount();
        var (site, slug) = await NewSiteAsync("case", (account, SiteRole.Owner));

        await using var scope = _services.CreateAsyncScope();
        var memberships = Memberships(scope);

        Assert.Equal(site, (await memberships.FindAsync(account, slug.ToUpperInvariant(), Timeout))?.SiteId);
        Assert.Null(await memberships.FindAsync(account, slug + " ", Timeout));
        Assert.Null(await memberships.FindAsync(account, slug.Replace('a', 'а'), Timeout)); // Cyrillic а
    }

    [Fact]
    public async Task A_suspended_membership_and_a_deleted_site_reach_nothing()
    {
        var suspended = NewAccount();
        var owner = NewAccount();
        var (site, slug) = await NewSiteAsync("gone", (owner, SiteRole.Owner), (suspended, SiteRole.Editor));
        await ChangeAsync(db => Tracked(db, site, suspended).Status = SiteMemberStatus.Suspended);

        await using (var scope = _services.CreateAsyncScope())
        {
            Assert.Null(await Memberships(scope).FindAsync(suspended, slug, Timeout));
            Assert.NotNull(await Memberships(scope).FindAsync(owner, slug, Timeout));
        }

        await ChangeAsync(db => db.Set<Site>().Single(s => s.Id == site).Status = SiteStatus.Deleted);

        await using (var scope = _services.CreateAsyncScope())
        {
            Assert.Null(await Memberships(scope).FindAsync(owner, slug, Timeout));
        }
    }

    private static AccountId NewAccount() => new(Guid.NewGuid());

    // Lower-case hex keeps every slug inside the shape and unique to this run, since the database is shared.
    private static string UniqueSlug(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static SiteMember Member(SiteId site, AccountId account, SiteRole role) => new()
    {
        SiteId = site,
        AccountId = account,
        Role = role,
        Status = SiteMemberStatus.Active,
        CreatedAt = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero),
    };

    private static SiteMember Tracked(PantryPressDbContext db, SiteId site, AccountId account) =>
        db.Set<SiteMember>().Single(m => m.SiteId == site && m.AccountId == account);

    private async Task<(SiteId Site, AccountId Owner)> SiteWithOwnerAsync()
    {
        var owner = NewAccount();
        var (site, _) = await NewSiteAsync("own", (owner, SiteRole.Owner));
        return (site, owner);
    }

    private async Task<(SiteId Site, string Slug)> NewSiteAsync(string prefix, params (AccountId Account, SiteRole Role)[] members)
    {
        var slug = UniqueSlug(prefix);
        var site = new Site
        {
            Id = new SiteId(Guid.NewGuid()),
            Slug = slug,
            Name = "Sunday Supper",
            Tagline = "Weeknight dinners",
            Status = SiteStatus.Live,
            TimeZone = "America/Chicago",
            Locale = "en-US",
            CreatedAt = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero),
        };

        // A site and its first owner are saved together, as creating a site will (PP2.10).
        await ChangeAsync(db =>
        {
            db.Add(site);
            db.AddRange(members.Select(m => Member(site.Id, m.Account, m.Role)));
        });
        return (site.Id, slug);
    }

    private async Task<AccountId> AddMemberAsync(SiteId site, SiteRole role)
    {
        var account = NewAccount();
        await ChangeAsync(db => db.Add(Member(site, account, role)));
        return account;
    }

    private async Task<SiteMember?> FindMemberAsync(SiteId site, AccountId account)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<SiteMember>()
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.SiteId == site && m.AccountId == account, Timeout);
    }

    private async Task ChangeAsync(Action<PantryPressDbContext> change)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        change(db);
        await db.SaveChangesAsync(Timeout);
    }

    /// <summary>The SQL error a change was refused with, or null when it was saved.</summary>
    private async Task<int?> TryChangeAsync(Action<PantryPressDbContext> change)
    {
        try
        {
            await ChangeAsync(change);
            return null;
        }
        // EF Core reports a deadlock victim as a transient failure wrapped around the update's own exception.
        catch (Exception error) when (SqlError(error) is { } sql)
        {
            return sql.Number;
        }
    }

    private static SqlException? SqlError(Exception? error)
    {
        for (; error is not null; error = error.InnerException)
        {
            if (error is SqlException sql)
            {
                return sql;
            }
        }

        return null;
    }

    private async Task<int> ActiveOwnersAsync(SiteId site)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<SiteMember>()
            .CountAsync(m => m.SiteId == site && m.Role == SiteRole.Owner && m.Status == SiteMemberStatus.Active, Timeout);
    }

    private ServiceProvider CountedServices(CountingCommands commands)
    {
        var services = new ServiceCollection();
        services.AddAccountsIdentitySchema();
        services.AddDbContext<PantryPressDbContext>(options => options
            .UseSqlServer(_connectionString, MigrationServiceExtensions.UseMigrationsAssembly)
            .AddInterceptors(commands));
        services.AddSites();
        return services.BuildServiceProvider();
    }

    private sealed class CountingCommands : DbCommandInterceptor
    {
        private int _count;

        public int Count => _count;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private async Task AssertRefusedAsync(int number, Action<PantryPressDbContext> change)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => ChangeAsync(change));
        Assert.Equal(number, Assert.IsType<SqlException>(error.InnerException).Number);
    }
}
