using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites;

/// <summary>
/// The reserved list's table (PP2.8), seeded from <see cref="ReservedSlugs.All"/>: a change to the list changes the model,
/// so it arrives only through a reviewed migration.
/// </summary>
internal sealed class ReservedSlugConfiguration : IEntityTypeConfiguration<ReservedSlug>
{
    public const string TableName = "ReservedSlugs";
    public const string ReasonConstraintName = "CK_ReservedSlugs_Reason";

    /// <summary>
    /// Created by the ReservedSlugs migration: refuses reserving a slug a site holds, a deleted site included, since it
    /// still holds the unique index. A migration that adds such a slug fails, so taking an address from a site is decided
    /// on purpose, with a plan for that site, and never by a list edit. Its message names the slug, for the operator whose
    /// migration it stopped; it never reaches a client.
    /// </summary>
    public const string NotHeldTriggerName = "TR_ReservedSlugs_NotHeld";

    public const int NotHeldErrorNumber = 51006;

    public void Configure(EntityTypeBuilder<ReservedSlug> builder)
    {
        builder.ToTable(TableName, table =>
        {
            table.HasTrigger(NotHeldTriggerName);

            // Built from the enum, so a new reason changes the model and gets its migration.
            table.HasCheckConstraint(
                ReasonConstraintName,
                $"[Reason] IN ({string.Join(", ", Enum.GetValues<ReservedSlugReason>().Select(r => (int)r))})");
        });

        builder.HasKey(r => r.Slug);

        // The same column as Sites.Slug, so the triggers compare the two byte for byte.
        builder.Property(r => r.Slug)
            .HasMaxLength(SiteLimits.MaxSlugLength)
            .IsUnicode(false)
            .UseCollation(SiteConfiguration.BinaryCollation);

        builder.HasData(ReservedSlugs.All.Select(e => new ReservedSlug { Slug = e.Slug, Reason = e.Reason, Since = e.Since }));
    }
}
