using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites;

/// <summary>The host table (PP2.9).</summary>
internal sealed class SiteHostConfiguration : IEntityTypeConfiguration<SiteHost>
{
    public const string TableName = "SiteHosts";
    public const string HostIndexName = "UX_SiteHosts_Host";
    public const string CanonicalIndexName = "UX_SiteHosts_SiteId_Canonical";
    public const string SiteIndexName = "IX_SiteHosts_SiteId_Kind";
    public const string HostShapeConstraintName = "CK_SiteHosts_Host_Shape";
    public const string KindConstraintName = "CK_SiteHosts_Kind";
    public const string StatusConstraintName = "CK_SiteHosts_Status";
    public const string CanonicalConstraintName = "CK_SiteHosts_Canonical";

    /// <summary>
    /// Created by the SiteHosts migration. It refuses any update or delete that leaves a site that is not deleted without a
    /// canonical host (<see cref="KeepACanonicalErrorNumber"/>), and any change to a row's site or host, or that makes a host
    /// a subdomain or stops it being one (<see cref="FixedHostErrorNumber"/>): a subdomain is the platform's own address,
    /// never a custom domain that needs verifying. It guards losing a canonical host, not having one: the facade that creates a
    /// site writes its subdomain with it (PP2.10). Moving the canonical host is therefore one statement that clears the old
    /// and sets the new together - the filtered unique index refuses the new one first, and this the old one cleared first.
    /// Two moves on one site at the same instant can deadlock instead (1205), and one is rolled back; a caller answers both.
    /// Declaring it keeps EF Core from using an OUTPUT clause that a table with triggers cannot take.
    /// </summary>
    public const string GuardTriggerName = "TR_SiteHosts_Guard";

    public const int KeepACanonicalErrorNumber = 51007;

    public const int FixedHostErrorNumber = 51008;

    public void Configure(EntityTypeBuilder<SiteHost> builder)
    {
        builder.ToTable(TableName, table =>
        {
            table.HasTrigger(GuardTriggerName);

            // A backstop under SiteHostNames: lower-case letters, digits, dots, and hyphens, so no port, no trailing dot, no
            // capital, and no IPv6 literal; at least two labels, none empty, longer than 63, or with a hyphen at either end;
            // and a last label that starts with a letter, as every top-level domain does, so no IPv4 literal in any of its
            // spellings - decimal, octal, or hex (127.0.0.0x1).
            table.HasCheckConstraint(
                HostShapeConstraintName,
                $"DATALENGTH([Host]) BETWEEN 3 AND {SiteLimits.MaxHostLength} " +
                $"AND [Host] NOT LIKE '%[^a-z0-9.-]%' COLLATE {SiteConfiguration.BinaryCollation} " +
                "AND [Host] LIKE '%_._%' " +
                "AND [Host] NOT LIKE '.%' AND [Host] NOT LIKE '%.' AND [Host] NOT LIKE '%..%' " +
                "AND [Host] NOT LIKE '-%' AND [Host] NOT LIKE '%-' AND [Host] NOT LIKE '%.-%' AND [Host] NOT LIKE '%-.%' " +
                $"AND [Host] NOT LIKE '%{string.Concat(Enumerable.Repeat("[^.]", SiteLimits.MaxHostLabelLength + 1))}%' " +
                "AND SUBSTRING(REVERSE([Host]), CHARINDEX('.', REVERSE([Host])) - 1, 1) LIKE '[a-z]'");

            // Built from the enums, so a new value changes the model and gets its migration.
            table.HasCheckConstraint(
                KindConstraintName,
                $"[Kind] IN ({string.Join(", ", Enum.GetValues<SiteHostKind>().Select(k => (int)k))})");
            table.HasCheckConstraint(
                StatusConstraintName,
                $"[Status] IN ({string.Join(", ", Enum.GetValues<SiteHostStatus>().Select(s => (int)s))})");
            table.HasCheckConstraint(
                CanonicalConstraintName,
                $"[IsCanonical] = 0 OR ([Status] = {(int)SiteHostStatus.Active} AND [Kind] <> {(int)SiteHostKind.WwwVariant})");
        });

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).HasConversion(id => id.Value, value => new SiteHostId(value)).ValueGeneratedNever();

        // Hosts compare byte for byte, as slugs do (PP2.2), and the shape check admits only lower-case, so the binary
        // index is as strict as a case-insensitive one.
        builder.Property(h => h.Host)
            .HasMaxLength(SiteLimits.MaxHostLength)
            .IsUnicode(false)
            .UseCollation(SiteConfiguration.BinaryCollation);

        // The written exception to SiteId-leading indexes: the public tier starts from the Host, before any site is known
        // (tenancy.md, "Tenant root"). A host belongs to one site platform-wide (DOM-004).
        builder.HasIndex(h => h.Host).IsUnique().HasDatabaseName(HostIndexName);

        // At most one canonical host per site; the trigger keeps at least one.
        builder.HasIndex(h => h.SiteId)
            .IsUnique()
            .HasFilter("[IsCanonical] = 1")
            .HasDatabaseName(CanonicalIndexName);

        // A site's hosts, listed or moved together, and the foreign key's own.
        builder.HasIndex(h => new { h.SiteId, h.Kind }).HasDatabaseName(SiteIndexName);

        // A site is never deleted, only marked Deleted (PP2.2), so nothing cascades from it.
        builder.HasOne<Site>().WithMany().HasForeignKey(h => h.SiteId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(h => h.Version).IsRowVersion();
    }
}
