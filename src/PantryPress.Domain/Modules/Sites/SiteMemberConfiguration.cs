using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites;

/// <summary>
/// The membership table (PP2.3). No foreign key to the accounts table: a module never names another's entities, and
/// account erasure (PP19.11a) removes an account's memberships itself.
/// </summary>
internal sealed class SiteMemberConfiguration : IEntityTypeConfiguration<SiteMember>
{
    public const string TableName = "SiteMembers";
    public const string AccountIndexName = "IX_SiteMembers_AccountId";
    public const string RoleConstraintName = "CK_SiteMembers_Role";
    public const string StatusConstraintName = "CK_SiteMembers_Status";

    /// <summary>
    /// Created by the SiteMembers migration: refuses any update or delete of an active Owner that leaves a site that is not
    /// deleted with none. It is what holds when two owners demote each other at once, which no check made before the save
    /// can see; two at the same instant deadlock instead, and one is rolled back. It guards losing an owner, not having
    /// one: the facade that creates a site writes its first Owner with it (PP2.10). Declaring it keeps EF Core from using
    /// an OUTPUT clause that a table with triggers cannot take.
    /// </summary>
    public const string KeepAnOwnerTriggerName = "TR_SiteMembers_KeepAnOwner";

    /// <summary>
    /// The error number the trigger raises. PP2.11's member changes read it as <c>SiteErrors.LastOwner</c>, and owe an
    /// answer for a deadlock (1205) too; either ends the whole transaction.
    /// </summary>
    public const int KeepAnOwnerErrorNumber = 51003;

    public void Configure(EntityTypeBuilder<SiteMember> builder)
    {
        builder.ToTable(TableName, table =>
        {
            table.HasTrigger(KeepAnOwnerTriggerName);

            // Built from the enums, so a new value changes the model and gets its migration.
            table.HasCheckConstraint(
                RoleConstraintName,
                $"[Role] IN ({string.Join(", ", Enum.GetValues<SiteRole>().Select(r => (int)r))})");
            table.HasCheckConstraint(
                StatusConstraintName,
                $"[Status] IN ({string.Join(", ", Enum.GetValues<SiteMemberStatus>().Select(s => (int)s))})");
        });

        // One membership per account per site, and the site leads (tenancy.md rule 8's index rule).
        builder.HasKey(m => new { m.SiteId, m.AccountId });
        builder.Property(m => m.SiteId).HasConversion(id => id.Value, value => new SiteId(value));
        builder.Property(m => m.AccountId).HasConversion(id => id.Value, value => new AccountId(value));

        // The written exception to SiteId-leading indexes: resolving an admin route and listing the caller's sites both
        // start from the account, before any site is known (tenancy.md, "Tenant root").
        builder.HasIndex(m => m.AccountId).HasDatabaseName(AccountIndexName);

        // A site is never deleted, only marked Deleted (PP2.2), so nothing cascades from it.
        builder.HasOne<Site>().WithMany().HasForeignKey(m => m.SiteId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(m => m.Version).IsRowVersion();
    }
}
