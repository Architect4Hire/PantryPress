using System.Data.Common;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Accounts.Business;
using PantryPress.Domain.Modules.Accounts.Repositories;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Persistence;

// PP1.11: the audit log against the schema the migration service applies. An entry commits with the save it was staged
// for and is lost with it, and once written it can be neither changed nor removed - not through the context, and not
// through raw or bulk SQL that never passes the context, which the table's trigger refuses.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class AuditLogTests(MigratedDatabase database) : IAsyncLifetime
{
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
        services.AddApplicationClock();
        services.AddAuditLog();
        _services = services.BuildServiceProvider();
    }

    public Task DisposeAsync() => _services.DisposeAsync().AsTask();

    [Fact]
    public async Task An_entry_commits_with_the_save_it_was_staged_for()
    {
        var target = NewTarget();
        var site = new SiteId(Guid.NewGuid());

        await using (var scope = _services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ISiteContextWriter>().Enter(site);
            Audit(scope).Record(new AuditEvent("site.test-action", AuditActor.System, target, "a stated reason"), SiteScope.Current);
            await Db(scope).SaveChangesAsync(Timeout);
        }

        var entry = Assert.Single(await EntriesAsync(target));
        Assert.True(entry.Id > 0);
        Assert.Equal("site.test-action", entry.Action);
        Assert.Equal("system", entry.ActorKind);
        Assert.Equal(site.Value, entry.SiteId);
        Assert.Equal("a stated reason", entry.Reason);
        Assert.Equal(TimeSpan.Zero, entry.OccurredAt.Offset);
    }

    [Fact]
    public async Task An_entry_is_lost_with_a_save_that_fails()
    {
        var target = NewTarget();
        var taken = $"taken-{Guid.NewGuid():N}";
        await using (var scope = _services.CreateAsyncScope())
        {
            Db(scope).Users.Add(NewUser(taken));
            await Db(scope).SaveChangesAsync(Timeout);
        }

        await using (var scope = _services.CreateAsyncScope())
        {
            // The same save as an account that loses the unique user-name index, so the whole save is refused.
            Audit(scope).Record(new AuditEvent("account.test-action", AuditActor.System, target), SiteScope.None);
            Db(scope).Users.Add(NewUser(taken));
            await Assert.ThrowsAsync<DbUpdateException>(() => Db(scope).SaveChangesAsync(Timeout));
        }

        Assert.Empty(await EntriesAsync(target));
    }

    [Fact]
    public async Task An_entry_is_lost_with_a_transaction_that_rolls_back()
    {
        var target = NewTarget();

        await using (var scope = _services.CreateAsyncScope())
        {
            var db = Db(scope);
            await using var transaction = await db.Database.BeginTransactionAsync(Timeout);
            Audit(scope).Record(new AuditEvent("account.test-action", AuditActor.System, target), SiteScope.None);
            await db.SaveChangesAsync(Timeout);
            await transaction.RollbackAsync(Timeout);
        }

        Assert.Empty(await EntriesAsync(target));
    }

    // The guarantee two racing changes to one account rely on: the one that loses the concurrency check leaves no entry.
    [Fact]
    public async Task An_entry_is_lost_with_a_save_that_loses_a_concurrency_race()
    {
        var target = NewTarget();
        var name = $"raced-{Guid.NewGuid():N}";
        var user = NewUser(name);
        await using (var scope = _services.CreateAsyncScope())
        {
            Db(scope).Users.Add(user);
            await Db(scope).SaveChangesAsync(Timeout);
        }

        await using var loser = _services.CreateAsyncScope();
        var stale = await Db(loser).Users.SingleAsync(u => u.Id == user.Id, Timeout);
        await using (var winner = _services.CreateAsyncScope())
        {
            var fresh = await Db(winner).Users.SingleAsync(u => u.Id == user.Id, Timeout);
            fresh.SecurityStamp = Guid.NewGuid().ToString("N");
            fresh.ConcurrencyStamp = Guid.NewGuid().ToString();
            await Db(winner).SaveChangesAsync(Timeout);
        }

        stale.SecurityStamp = Guid.NewGuid().ToString("N");
        stale.ConcurrencyStamp = Guid.NewGuid().ToString();
        Audit(loser).Record(new AuditEvent("account.test-action", AuditActor.System, target), SiteScope.None);
        var outcome = await new AccountsUnitOfWork(Db(loser), new UpperInvariantLookupNormalizer()).SaveAsync(Timeout);

        Assert.Equal(SaveOutcome.Conflict, outcome);
        Assert.Empty(Db(loser).ChangeTracker.Entries<AuditEntry>());
        Assert.Empty(await EntriesAsync(target));
    }

    [Fact]
    public async Task Entries_are_numbered_in_the_order_they_were_written()
    {
        var target = NewTarget();
        for (var i = 0; i < 3; i++)
        {
            await using var scope = _services.CreateAsyncScope();
            Audit(scope).Record(new AuditEvent($"account.step-{i}", AuditActor.System, target), SiteScope.None);
            await Db(scope).SaveChangesAsync(Timeout);
        }

        var entries = await EntriesAsync(target);
        Assert.Equal(["account.step-0", "account.step-1", "account.step-2"], entries.Select(e => e.Action));
        Assert.True(entries[0].Id < entries[1].Id && entries[1].Id < entries[2].Id);
    }

    [Fact]
    public async Task The_context_refuses_to_change_or_remove_a_stored_entry()
    {
        var target = await WriteOneAsync();

        await using (var scope = _services.CreateAsyncScope())
        {
            var entry = await Db(scope).Set<AuditEntry>().SingleAsync(e => e.TargetId == target.Id, Timeout);
            entry.Action = "account.rewritten";
            await Assert.ThrowsAsync<InvalidOperationException>(() => Db(scope).SaveChangesAsync(Timeout));
        }

        await using (var scope = _services.CreateAsyncScope())
        {
            var entry = await Db(scope).Set<AuditEntry>().SingleAsync(e => e.TargetId == target.Id, Timeout);
            Db(scope).Remove(entry);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Db(scope).SaveChangesAsync(Timeout));
        }

        Assert.Equal("account.test-action", Assert.Single(await EntriesAsync(target)).Action);
    }

    [Fact]
    public async Task The_database_refuses_bulk_updates_and_deletes_that_bypass_the_context()
    {
        var target = await WriteOneAsync();

        await using (var scope = _services.CreateAsyncScope())
        {
            var entries = Db(scope).Set<AuditEntry>().Where(e => e.TargetId == target.Id);
            await AssertRefusedAsync(() => entries.ExecuteUpdateAsync(s => s.SetProperty(e => e.Reason, "rewritten"), Timeout));
            await AssertRefusedAsync(() => entries.ExecuteDeleteAsync(Timeout));
        }

        Assert.Single(await EntriesAsync(target));
    }

    [Fact]
    public async Task The_database_refuses_raw_updates_and_deletes()
    {
        var target = await WriteOneAsync();

        await using (var scope = _services.CreateAsyncScope())
        {
            var db = Db(scope).Database;
            await AssertRefusedAsync(() => db.ExecuteSqlAsync($"UPDATE [AuditEntries] SET [Reason] = N'rewritten' WHERE [TargetId] = {target.Id}", Timeout));
            await AssertRefusedAsync(() => db.ExecuteSqlAsync($"DELETE FROM [AuditEntries] WHERE [TargetId] = {target.Id}", Timeout));
        }

        var entry = Assert.Single(await EntriesAsync(target));
        Assert.Null(entry.Reason);
    }

    [Fact]
    public async Task The_trigger_is_in_place_and_enabled()
    {
        await using var scope = _services.CreateAsyncScope();

        var enabled = await Db(scope).Database
            .SqlQuery<bool>($"""
                SELECT CAST(CASE WHEN t.is_disabled = 0 THEN 1 ELSE 0 END AS bit) AS [Value]
                FROM sys.triggers t
                WHERE t.name = {AuditEntryConfiguration.AppendOnlyTriggerName}
                  AND t.parent_id = OBJECT_ID(N'AuditEntries')
                """)
            .ToListAsync(Timeout);

        Assert.True(Assert.Single(enabled));
    }

    private static async Task AssertRefusedAsync(Func<Task> statement)
    {
        var error = await Assert.ThrowsAnyAsync<DbException>(statement);
        Assert.Contains("append-only", error.Message, StringComparison.Ordinal);
    }

    private static AuditTarget NewTarget() => new("test-thing", Guid.NewGuid().ToString("D"));

    private static ApplicationUser NewUser(string name) => new()
    {
        Id = Guid.NewGuid(),
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        NormalizedEmail = $"{name}@example.test".ToUpperInvariant(),
        SecurityStamp = Guid.NewGuid().ToString("N"),
    };

    private static IAuditLog Audit(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IAuditLog>();

    private static PantryPressDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();

    private async Task<AuditTarget> WriteOneAsync()
    {
        var target = NewTarget();
        await using var scope = _services.CreateAsyncScope();
        Audit(scope).Record(new AuditEvent("account.test-action", AuditActor.System, target), SiteScope.None);
        await Db(scope).SaveChangesAsync(Timeout);
        return target;
    }

    private async Task<List<AuditEntry>> EntriesAsync(AuditTarget target)
    {
        await using var scope = _services.CreateAsyncScope();
        return await Db(scope).Set<AuditEntry>()
            .AsNoTracking()
            .Where(e => e.TargetKind == target.Kind && e.TargetId == target.Id)
            .OrderBy(e => e.Id)
            .ToListAsync(Timeout);
    }
}
