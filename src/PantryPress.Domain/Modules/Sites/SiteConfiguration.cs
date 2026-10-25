using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites;

/// <summary>
/// The tenant root's table (PP2.2). Nothing refers to it yet; what does from PP2.3 on uses <c>Restrict</c>, never a
/// cascade, because a site is never deleted, only marked <see cref="SiteStatus.Deleted"/>.
/// </summary>
internal sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public const string TableName = "Sites";
    public const string SlugIndexName = "UX_Sites_Slug";
    public const string SlugShapeConstraintName = "CK_Sites_Slug_Shape";
    public const string StatusConstraintName = "CK_Sites_Status";

    /// <summary>
    /// Slugs compare byte for byte, so uniqueness never depends on the server's default collation. The shape check
    /// admits only lower-case, so the binary index is as strict as a case-insensitive one: "Pie" cannot sit beside "pie".
    /// </summary>
    public const string BinaryCollation = "Latin1_General_100_BIN2";

    public void Configure(EntityTypeBuilder<Site> builder)
    {
        builder.ToTable(TableName, table =>
        {
            // A backstop under PP2.8's slug policy, which owns the reserved list and the finer rules.
            table.HasCheckConstraint(
                SlugShapeConstraintName,
                $"LEN([Slug]) BETWEEN {SiteLimits.MinSlugLength} AND {SiteLimits.MaxSlugLength} " +
                $"AND [Slug] NOT LIKE '%[^a-z0-9-]%' COLLATE {BinaryCollation}");
            // Built from the enum, so a new status changes the model and gets its migration.
            table.HasCheckConstraint(
                StatusConstraintName,
                $"[Status] IN ({string.Join(", ", Enum.GetValues<SiteStatus>().Select(s => (int)s))})");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasConversion(id => id.Value, value => new SiteId(value)).ValueGeneratedNever();

        builder.Property(s => s.Slug)
            .HasMaxLength(SiteLimits.MaxSlugLength)
            .IsUnicode(false)
            .UseCollation(BinaryCollation);
        builder.HasIndex(s => s.Slug).IsUnique().HasDatabaseName(SlugIndexName);

        builder.Property(s => s.Name).HasMaxLength(SiteLimits.MaxNameLength);
        builder.Property(s => s.Tagline).HasMaxLength(SiteLimits.MaxTaglineLength);
        builder.Property(s => s.TimeZone).HasMaxLength(SiteLimits.MaxTimeZoneLength).IsUnicode(false);
        builder.Property(s => s.Locale).HasMaxLength(SiteLimits.MaxLocaleLength).IsUnicode(false);
        builder.Property(s => s.Version).IsRowVersion();
    }
}
