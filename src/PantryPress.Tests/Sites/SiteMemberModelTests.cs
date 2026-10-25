using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Sites;

// Inside the namespace, so the types win over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;

// PP2.3: membership (SITE-010) is what grants a site, so like the site itself it has no query filter (tenancy.md,
// "Tenant root"); it carries a required SiteId with a foreign key that never cascades. No database is needed.
public sealed class SiteMemberModelTests
{
    // The design-time model: the runtime one drops check constraints and triggers, which only migrations need.
    private static IEntityType MemberEntity(DbContext db) =>
        db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(SiteMember))!;

    [Fact]
    public void One_membership_per_account_per_site_with_the_site_leading()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var member = MemberEntity(db);

        Assert.Equal(SiteMemberConfiguration.TableName, member.GetTableName());
        Assert.Equal([nameof(SiteMember.SiteId), nameof(SiteMember.AccountId)], member.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(typeof(SiteId), member.FindProperty(nameof(SiteMember.SiteId))!.ClrType);
        Assert.Equal(typeof(AccountId), member.FindProperty(nameof(SiteMember.AccountId))!.ClrType);
        Assert.Empty(member.GetDeclaredQueryFilters());
        Assert.All(member.GetProperties(), p => Assert.False(p.IsNullable, $"{p.Name} is optional."));
    }

    // The written exception to SiteId-leading indexes: resolution and "my sites" start from the account. The site's
    // foreign key needs no index of its own, because the primary key leads with it.
    [Fact]
    public void The_account_index_is_the_only_index_beside_the_key()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        var index = Assert.Single(MemberEntity(db).GetIndexes());
        Assert.Equal([nameof(SiteMember.AccountId)], index.Properties.Select(p => p.Name));
        Assert.False(index.IsUnique);
        Assert.Equal(SiteMemberConfiguration.AccountIndexName, index.GetDatabaseName());
    }

    [Fact]
    public void Its_site_is_a_foreign_key_that_never_cascades_and_no_account_key_crosses_modules()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        var key = Assert.Single(MemberEntity(db).GetForeignKeys());
        Assert.Equal(typeof(Site), key.PrincipalEntityType.ClrType);
        Assert.Equal([nameof(SiteMember.SiteId)], key.Properties.Select(p => p.Name));
        Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior);
    }

    [Fact]
    public void It_declares_its_trigger_checks_and_row_version()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var member = MemberEntity(db);
        var version = member.FindProperty(nameof(SiteMember.Version))!;

        Assert.Equal([SiteMemberConfiguration.KeepAnOwnerTriggerName], member.GetDeclaredTriggers().Select(t => t.GetDatabaseName()));
        Assert.Equal(
            [SiteMemberConfiguration.RoleConstraintName, SiteMemberConfiguration.StatusConstraintName],
            member.GetCheckConstraints().Select(c => c.Name).Order());
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal("rowversion", version.GetColumnType());
    }

    // Roles and statuses are stored as numbers, and the trigger's SQL names Owner and Active as 0: the numbers are schema.
    [Fact]
    public void Role_and_status_numbers_are_fixed()
    {
        Assert.Equal(
            [(SiteRole.Owner, 0), (SiteRole.Editor, 1), (SiteRole.Author, 2)],
            Enum.GetValues<SiteRole>().Select(r => (r, (int)r)));
        Assert.Equal(
            [(SiteMemberStatus.Active, 0), (SiteMemberStatus.Suspended, 1)],
            Enum.GetValues<SiteMemberStatus>().Select(s => (s, (int)s)));
        Assert.Equal(5, (int)SiteStatus.Deleted);
    }
}
