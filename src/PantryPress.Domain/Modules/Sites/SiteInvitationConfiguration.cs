using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PantryPress.Domain.Managers.Email;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites;

/// <summary>
/// The invitations table (PP2.11). The site leads the key and every index (tenancy.md rule 8's index rule), and the filter
/// and stamp come from <c>ISiteOwned</c> by convention. No foreign key to the accounts table: a module never names
/// another's entities, and the invited person may have no account yet.
/// </summary>
internal sealed class SiteInvitationConfiguration : IEntityTypeConfiguration<SiteInvitation>
{
    public const string TableName = "SiteInvitations";
    public const string PendingAddressIndexName = "UX_SiteInvitations_SiteId_NormalizedEmail_Pending";
    public const string RoleConstraintName = "CK_SiteInvitations_Role";
    public const string StatusConstraintName = "CK_SiteInvitations_Status";
    public const string AcceptedConstraintName = "CK_SiteInvitations_Accepted";
    public const string ExpiryConstraintName = "CK_SiteInvitations_Expiry";

    private static readonly ValueConverter<AccountId, Guid> AccountIdConverter = new(id => id.Value, value => new AccountId(value));

    public void Configure(EntityTypeBuilder<SiteInvitation> builder)
    {
        builder.ToTable(TableName, table =>
        {
            // Built from the enums, so a new value changes the model and gets its migration.
            table.HasCheckConstraint(
                RoleConstraintName,
                $"[Role] IN ({string.Join(", ", Enum.GetValues<SiteRole>().Select(r => (int)r))})");
            table.HasCheckConstraint(
                StatusConstraintName,
                $"[Status] IN ({string.Join(", ", Enum.GetValues<SiteInvitationStatus>().Select(s => (int)s))})");

            // Accepted, and only accepted, says by whom and when.
            var accepted = (int)SiteInvitationStatus.Accepted;
            table.HasCheckConstraint(
                AcceptedConstraintName,
                $"([Status] = {accepted} AND [AcceptedBy] IS NOT NULL AND [AcceptedAt] IS NOT NULL) "
                + $"OR ([Status] <> {accepted} AND [AcceptedBy] IS NULL AND [AcceptedAt] IS NULL)");
            table.HasCheckConstraint(ExpiryConstraintName, "[ExpiresAt] > [CreatedAt]");
        });

        builder.HasKey(i => new { i.SiteId, i.Id });
        builder.Property(i => i.Id).HasConversion(id => id.Value, value => new SiteInvitationId(value)).ValueGeneratedNever();
        builder.Property(i => i.InvitedBy).HasConversion(AccountIdConverter);
        builder.Property(i => i.AcceptedBy).HasConversion(AccountIdConverter);

        builder.Property(i => i.Email).HasMaxLength(EmailAddresses.MaxLength);
        builder.Property(i => i.NormalizedEmail).HasMaxLength(EmailAddresses.MaxLength);

        // At most one pending invitation per address on a site: inviting again revokes the old one in the same transaction,
        // under a lock that makes two at once take turns, and this is the database's word on it. It also serves counting a
        // site's open invitations.
        builder.HasIndex(i => new { i.SiteId, i.NormalizedEmail })
            .IsUnique()
            .HasFilter($"[Status] = {(int)SiteInvitationStatus.Pending}")
            .HasDatabaseName(PendingAddressIndexName);

        // A site is never deleted, only marked Deleted (PP2.2), so nothing cascades from it.
        builder.HasOne<Site>().WithMany().HasForeignKey(i => i.SiteId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(i => i.Version).IsRowVersion();
    }
}
