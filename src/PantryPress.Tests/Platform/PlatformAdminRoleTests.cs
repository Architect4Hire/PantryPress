using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Platform.Contracts;
using PantryPress.Tests.Persistence;

namespace PantryPress.Tests.Platform;

// PP1.4: the platform role against the schema the migration service applies, through the operator's own path
// (ACCT-005). The role is platform data: no SiteId, one row, the same in every environment (PP-D06).
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class PlatformAdminRoleTests(MigratedDatabase database)
{
    private const string Password = "Correct-Horse-Battery-1";

    [Fact]
    public async Task The_role_is_seeded_once_by_a_migration_and_holds_no_site()
    {
        await using var services = await OperatorServicesAsync(admins: string.Empty);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();

        var roles = await db.Roles.ToListAsync(Timeout);

        var role = Assert.Single(roles, r => r.NormalizedName == PlatformRoles.PlatformAdminNormalized);
        Assert.Equal(PlatformRoles.PlatformAdminId, role.Id);
        Assert.Equal(PlatformRoles.PlatformAdmin, role.Name);
        Assert.DoesNotContain(typeof(IdentityRole<Guid>).GetProperties(), p => p.Name == "SiteId");
    }

    [Fact]
    public async Task A_grant_gives_the_role_ends_other_sessions_and_is_audited_once()
    {
        await using var services = await OperatorServicesAsync(admins: string.Empty);
        var email = NewEmail();
        var (account, stampBeforeGrant) = await CreateAccountAsync(services, email);

        var granted = await GrantAsync(services, email);
        var stampAfterGrant = await SecurityStampAsync(services, account);
        var again = await GrantAsync(services, email);
        var stampAfterRepeat = await SecurityStampAsync(services, account);

        Assert.True(granted.Value.Changed);
        Assert.Equal(account, granted.Value.Account);
        Assert.True(await IsPlatformAdminAsync(services, account));
        Assert.NotEqual(stampBeforeGrant, stampAfterGrant);

        // Running the operator's list again must not log anyone out.
        Assert.False(again.Value.Changed);
        Assert.Equal(stampAfterGrant, stampAfterRepeat);
        var audit = Assert.Single(AuditLines(services, "PlatformAdminGranted"));
        Assert.Contains(account.Value.ToString(), audit.Message, StringComparison.Ordinal);
        Assert.Contains(PlatformAdminActors.OperatorConfiguration.ToString(), audit.Message, StringComparison.Ordinal);

        // PP1.11: and durably, saved with the grant itself.
        var entry = Assert.Single(await AuditEntriesAsync(services, account));
        Assert.Equal(AccountAudit.PlatformAdminGranted, entry.Action);
        Assert.Equal("operator-configuration", entry.ActorKind);
        Assert.Null(entry.ActorId);
        Assert.Null(entry.SiteId);
        Assert.Equal("listed", entry.Reason);
        Assert.DoesNotContain(email, entry.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_revocation_takes_the_role_back_and_ends_the_sessions_that_carried_it()
    {
        await using var services = await OperatorServicesAsync(admins: string.Empty);
        var email = NewEmail();
        var (account, _) = await CreateAccountAsync(services, email);
        await GrantAsync(services, email);
        var stampAsAdmin = await SecurityStampAsync(services, account);

        var revoked = await RevokeAsync(services, account);

        Assert.True(revoked.Value.Changed);
        Assert.False(await IsPlatformAdminAsync(services, account));
        Assert.NotEqual(stampAsAdmin, await SecurityStampAsync(services, account));
        Assert.Single(AuditLines(services, "PlatformAdminRevoked"));
        Assert.Equal(
            [(AccountAudit.PlatformAdminGranted, "listed"), (AccountAudit.PlatformAdminRevoked, "unlisted")],
            (await AuditEntriesAsync(services, account)).Select(e => (e.Action, e.Reason)));
    }

    // PP1.11: an entry that would be refused is checked before the role change is staged, so nothing is left waiting on
    // the context for a later save to carry.
    [Fact]
    public async Task A_reason_the_audit_log_refuses_changes_nothing()
    {
        await using var services = await OperatorServicesAsync(admins: string.Empty);
        var email = NewEmail();
        var (account, stamp) = await CreateAccountAsync(services, email);

        await using (var scope = services.CreateAsyncScope())
        {
            await Assert.ThrowsAsync<ArgumentException>(() => scope.ServiceProvider.GetRequiredService<IPlatformAdminFacade>()
                .GrantAsync(email, PlatformAdminActors.OperatorConfiguration, $"asked for by {email}", Timeout));
            var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
            Assert.DoesNotContain(db.ChangeTracker.Entries(), e => e.State != EntityState.Unchanged);
        }

        Assert.False(await IsPlatformAdminAsync(services, account));
        Assert.Equal(stamp, await SecurityStampAsync(services, account));
        Assert.Empty(await AuditEntriesAsync(services, account));
    }

    [Fact]
    public async Task An_address_with_no_account_is_refused()
    {
        await using var services = await OperatorServicesAsync(admins: string.Empty);

        var outcome = await GrantAsync(services, NewEmail());

        Assert.False(outcome.IsSuccess);
        Assert.Equal(AccountErrors.AccountNotFound.Code, outcome.Error!.Code);
    }

    [Fact]
    public async Task The_operator_list_grants_the_listed_account_and_revokes_a_holder_it_leaves_out()
    {
        var listedEmail = NewEmail();
        var formerEmail = NewEmail();

        await using var setup = await OperatorServicesAsync(admins: string.Empty);
        var (listed, _) = await CreateAccountAsync(setup, listedEmail);
        var (former, _) = await CreateAccountAsync(setup, formerEmail);
        await GrantAsync(setup, formerEmail);

        await using var services = await OperatorServicesAsync($" {listedEmail} ,\n{listedEmail.ToUpperInvariant()}");
        var report = await ReconcileAsync(services);

        Assert.True(report.Configured);
        Assert.Equal(1, report.Granted);
        Assert.Equal(0, report.Unresolved);
        Assert.True(await IsPlatformAdminAsync(services, listed));
        Assert.False(await IsPlatformAdminAsync(services, former));
        Assert.Contains(AuditLines(services, "PlatformAdminRevoked"), r => r.Message.Contains(former.Value.ToString(), StringComparison.Ordinal));
        var granted = Assert.Single(await AuditEntriesAsync(services, listed));
        Assert.Equal(
            (AccountAudit.PlatformAdminGranted, "listed in the operator's PlatformAdmin list"),
            (granted.Action, granted.Reason));
        var revoked = (await AuditEntriesAsync(services, former)).Last();
        Assert.Equal(
            (AccountAudit.PlatformAdminRevoked, "no longer in the operator's PlatformAdmin list"),
            (revoked.Action, revoked.Reason));
    }

    [Fact]
    public async Task No_list_leaves_the_holders_exactly_as_they_are()
    {
        var email = NewEmail();
        await using var setup = await OperatorServicesAsync(admins: string.Empty);
        var (account, _) = await CreateAccountAsync(setup, email);
        await GrantAsync(setup, email);
        var stamp = await SecurityStampAsync(setup, account);

        await using var services = await OperatorServicesAsync(admins: "   ");
        var report = await ReconcileAsync(services);

        Assert.Equal(PlatformAdminReconcileReport.NotConfigured, report);
        Assert.True(await IsPlatformAdminAsync(services, account));
        Assert.Equal(stamp, await SecurityStampAsync(services, account));
        Assert.Single(await AuditEntriesAsync(services, account));
    }

    private static CancellationToken Timeout => CancellationToken.None;

    private static string NewEmail() => $"ops-{Guid.NewGuid():N}@example.test";

    private Task<ServiceProvider> OperatorServicesAsync(string admins) =>
        TestDatabase.CreateOperatorServicesAsync(database.App, admins, Timeout);

    private static async Task<(AccountId Account, string Stamp)> CreateAccountAsync(IServiceProvider services, string email)
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();

        var user = new ApplicationUser { UserName = email, Email = email };
        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Code)));
        await db.SaveChangesAsync(Timeout);

        return (new AccountId(user.Id), user.SecurityStamp!);
    }

    private static async Task<Domain.Managers.Results.Result<PlatformAdminOutcome>> GrantAsync(
        IServiceProvider services,
        string email)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPlatformAdminFacade>()
            .GrantAsync(email, PlatformAdminActors.OperatorConfiguration, "listed", Timeout);
    }

    private static async Task<Domain.Managers.Results.Result<PlatformAdminOutcome>> RevokeAsync(
        IServiceProvider services,
        AccountId account)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPlatformAdminFacade>()
            .RevokeAsync(account, PlatformAdminActors.OperatorConfiguration, "unlisted", Timeout);
    }

    private static async Task<PlatformAdminReconcileReport> ReconcileAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPlatformAdminReconciler>().RunAsync(Timeout);
    }

    private static async Task<bool> IsPlatformAdminAsync(IServiceProvider services, AccountId account)
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(account.ToString());
        return user is not null && await users.IsInRoleAsync(user, PlatformRoles.PlatformAdmin);
    }

    private static async Task<string> SecurityStampAsync(IServiceProvider services, AccountId account)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == account.Value, Timeout);
        return user.SecurityStamp!;
    }

    private static async Task<List<AuditEntry>> AuditEntriesAsync(IServiceProvider services, AccountId account)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<AuditEntry>()
            .AsNoTracking()
            .Where(e => e.TargetKind == AccountAudit.TargetKind && e.TargetId == account.ToString())
            .OrderBy(e => e.Id)
            .ToListAsync(Timeout);
    }

    private static IEnumerable<FakeLogRecord> AuditLines(IServiceProvider services, string eventName) =>
        services.GetFakeLogCollector().GetSnapshot().Where(r => r.Id.Name == eventName && r.Level == LogLevel.Information);
}
