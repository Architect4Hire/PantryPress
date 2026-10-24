using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Tests.Managers.Audit;

// PP1.11: what an audit entry may hold, what the writer stages, and the context's append-only guard - all before any
// database is involved.
public sealed class AuditRulesTests
{
    private static readonly AccountId Account = new(Guid.Parse("6f1c2a7e-3b4d-4c5e-8f90-112233445566"));
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 30, 0, TimeSpan.Zero);
    private static readonly FakeTimeProvider Clock = new(Now);

    [Theory]
    [InlineData("account.password-changed", true)]
    [InlineData("account.platform-admin-granted", true)]
    [InlineData("site.domain.verified", true)]
    [InlineData("account", false)]
    [InlineData("Account.PasswordChanged", false)]
    [InlineData("account..changed", false)]
    [InlineData("account.changed-", false)]
    [InlineData("account.changed now", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void An_action_is_lowercase_dot_separated_segments(string? action, bool valid) =>
        Assert.Equal(valid, AuditRules.IsValidAction(action));

    [Fact]
    public void An_action_has_a_length_limit() =>
        Assert.False(AuditRules.IsValidAction("account." + new string('a', AuditRules.MaxActionLength)));

    [Theory]
    [InlineData("account", "6f1c2a7e-3b4d-4c5e-8f90-112233445566", true)]
    [InlineData("publishing-key", "pk_1a2b3c", true)]
    [InlineData("account", "cook@example.test", false)]
    [InlineData("publishing-key", "pp_live_0123456789abcdef", false)]
    [InlineData("publishing-key", "PP_LIVE_0123456789abcdef", false)]
    [InlineData("account", "two words", false)]
    [InlineData("account", "", false)]
    [InlineData("Account", "x", false)]
    [InlineData("", "x", false)]
    public void A_target_is_a_kind_and_an_id_that_cannot_be_an_email_address(string kind, string id, bool valid) =>
        Assert.Equal(valid, AuditRules.IsValidTarget(new AuditTarget(kind, id)));

    [Theory]
    [InlineData(null, true)]
    [InlineData("listed in the operator's PlatformAdmin list", true)]
    [InlineData("", false)]
    [InlineData(" padded", false)]
    [InlineData("line\nbreak", false)]
    [InlineData("asked by ops.lead@example.test", false)]
    [InlineData("asked by ops.lead＠example.test", false)]
    [InlineData("asked by ops.lead﹫example.test", false)]
    public void A_reason_says_why_and_never_who(string? reason, bool valid) =>
        Assert.Equal(valid, AuditRules.IsValidReason(reason));

    // Built here rather than in an attribute, whose metadata cannot hold a lone surrogate.
    [Fact]
    public void A_reason_that_is_not_valid_text_is_refused_rather_than_thrown() =>
        Assert.False(AuditRules.IsValidReason("broken " + (char)0xD800 + " text"));

    [Fact]
    public void A_reason_has_a_length_limit()
    {
        Assert.True(AuditRules.IsValidReason(new string('a', AuditRules.MaxReasonLength)));
        Assert.False(AuditRules.IsValidReason(new string('a', AuditRules.MaxReasonLength + 1)));
    }

    [Fact]
    public void Actors_are_stored_as_stable_codes()
    {
        Assert.Equal("operator-configuration", AuditActor.OperatorConfiguration.ToString());
        Assert.Equal("system", AuditActor.System.ToString());
        Assert.Equal($"account:{Account}", AccountAudit.Actor(Account).ToString());
        Assert.Throws<ArgumentException>(() => AuditActor.Account(Guid.Empty));
    }

    [Fact]
    public void Recording_stages_one_entry_timed_by_the_application_clock_with_the_trace()
    {
        using var services = Services();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        using var activity = new Activity("audited").SetIdFormat(ActivityIdFormat.W3C).Start();

        scope.ServiceProvider.GetRequiredService<IAuditLog>().Record(
            new AuditEvent(AccountAudit.PlatformAdminGranted, AuditActor.OperatorConfiguration, AccountAudit.Target(Account), "listed"),
            siteId: null);

        var entry = Assert.Single(db.ChangeTracker.Entries<AuditEntry>());
        Assert.Equal(EntityState.Added, entry.State);
        Assert.Equal(Now, entry.Entity.OccurredAt);
        Assert.Equal(AccountAudit.PlatformAdminGranted, entry.Entity.Action);
        Assert.Equal("operator-configuration", entry.Entity.ActorKind);
        Assert.Null(entry.Entity.ActorId);
        Assert.Null(entry.Entity.SiteId);
        Assert.Equal(AccountAudit.TargetKind, entry.Entity.TargetKind);
        Assert.Equal(Account.ToString(), entry.Entity.TargetId);
        Assert.Equal("listed", entry.Entity.Reason);
        Assert.Equal(activity.TraceId.ToHexString(), entry.Entity.TraceId);
    }

    [Fact]
    public void An_account_actor_is_stored_by_id()
    {
        using var services = Services();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();

        scope.ServiceProvider.GetRequiredService<IAuditLog>().Record(
            AccountAudit.OwnChange(AccountAudit.PasswordChanged, Account),
            siteId: null);

        var entry = Assert.Single(db.ChangeTracker.Entries<AuditEntry>()).Entity;
        Assert.Equal("account", entry.ActorKind);
        Assert.Equal(Account.Value, entry.ActorId);
    }

    [Fact]
    public void Every_malformed_entry_is_refused_before_anything_is_staged()
    {
        using var services = Services();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var valid = AccountAudit.OwnChange(AccountAudit.PasswordChanged, Account);

        Assert.Throws<ArgumentNullException>(() => audit.Record(null!, siteId: null));
        Assert.Throws<ArgumentException>(() => audit.Record(valid with { Action = "Password changed" }, siteId: null));
        Assert.Throws<ArgumentException>(() => audit.Record(valid with { Actor = default }, siteId: null));
        Assert.Throws<ArgumentException>(() => audit.Record(valid with { Target = new AuditTarget("account", "cook@example.test") }, siteId: null));
        Assert.Throws<ArgumentException>(() => audit.Record(valid with { Reason = "for cook@example.test" }, siteId: null));
        Assert.Throws<ArgumentException>(() => audit.Record(valid, siteId: Guid.Empty));
        Assert.Empty(db.ChangeTracker.Entries<AuditEntry>());
    }

    [Fact]
    public void Entries_for_two_sites_keep_their_own_site()
    {
        using var services = Services();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var siteA = Guid.NewGuid();
        var siteB = Guid.NewGuid();

        audit.Record(new AuditEvent("site.test-action", AuditActor.System, new AuditTarget("post", "same-slug")), siteA);
        audit.Record(new AuditEvent("site.test-action", AuditActor.System, new AuditTarget("post", "same-slug")), siteB);

        var sites = db.ChangeTracker.Entries<AuditEntry>().Select(e => e.Entity.SiteId).ToList();
        Assert.Equal(2, sites.Count);
        Assert.Equal(new HashSet<Guid?> { siteA, siteB }, sites.ToHashSet());
    }

    [Fact]
    public async Task The_context_refuses_to_save_a_changed_entry()
    {
        using var services = Services();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var entry = StoredEntry(db);

        // Edited in place, with no explicit Update: change detection still sees it.
        entry.Reason = "rewritten";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("append-only", error.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task The_context_refuses_to_save_a_removed_entry()
    {
        using var services = Services();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        db.Remove(StoredEntry(db));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges(acceptAllChangesOnSuccess: false));
    }

    // An entry as a read would have tracked it.
    private static AuditEntry StoredEntry(PantryPressDbContext db)
    {
        var entry = new AuditEntry
        {
            Id = 42,
            OccurredAt = Now,
            Action = AccountAudit.PasswordChanged,
            ActorKind = "account",
            ActorId = Account.Value,
            TargetKind = AccountAudit.TargetKind,
            TargetId = Account.ToString(),
        };
        db.Attach(entry);
        return entry;
    }

    // An unopened SQL Server context: staging an entry, and refusing a change to one, never touch the database.
    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddAccountsIdentitySchema();
        services.AddDbContext<PantryPressDbContext>(o => o.UseSqlServer("Server=unused;Database=unused"));
        services.TryAddSingleton<TimeProvider>(Clock);
        services.AddApplicationClock();
        services.AddAuditLog();
        return services.BuildServiceProvider();
    }
}
