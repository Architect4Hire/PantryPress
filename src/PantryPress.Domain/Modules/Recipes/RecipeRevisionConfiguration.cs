using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// The recipe revisions table (PP4.4). A revision refers to its recipe by <c>(SiteId, RecipeId)</c>, so the database refuses
/// one under another site's recipe, and it is <c>Restrict</c>: a recipe that has a revision is archived, never deleted, and
/// its revisions never go with it. The trigger, created by the RecipeRevisions migration, refuses every update and delete in
/// the database; declaring it keeps EF Core from using an <c>OUTPUT</c> clause that a table with triggers cannot take.
/// </summary>
internal sealed class RecipeRevisionConfiguration : IEntityTypeConfiguration<RecipeRevision>
{
    public const string TableName = "RecipeRevisions";
    public const string ImmutableTriggerName = "TR_RecipeRevisions_Immutable";
    public const string NumberConstraintName = "CK_RecipeRevisions_Number";
    public const string KindConstraintName = "CK_RecipeRevisions_Kind";
    public const string SnapshotVersionConstraintName = "CK_RecipeRevisions_SnapshotVersion";
    public const string SnapshotConstraintName = "CK_RecipeRevisions_Snapshot";
    public const string NumberIndexName = "IX_RecipeRevisions_Number";
    public const string KindIndexName = "IX_RecipeRevisions_Kind";
    public const int ContentHashLength = 32;

    public void Configure(EntityTypeBuilder<RecipeRevision> builder)
    {
        builder.ToTable(TableName, table =>
        {
            table.HasTrigger(ImmutableTriggerName);
            table.HasCheckConstraint(NumberConstraintName, $"[{nameof(RecipeRevision.Number)}] >= 1");
            table.HasCheckConstraint(KindConstraintName, RecipeColumnConfiguration.InEnum<RecipeRevisionKind>(nameof(RecipeRevision.Kind)));
            table.HasCheckConstraint(SnapshotVersionConstraintName, $"[{nameof(RecipeRevision.SnapshotVersion)}] >= 1");
            table.HasCheckConstraint(SnapshotConstraintName, $"ISJSON([{nameof(RecipeRevision.Snapshot)}]) = 1");
        });

        builder.HasKey(r => new { r.SiteId, r.Id });
        builder.Property(r => r.Id).HasConversion(RecipeColumnConfiguration.RevisionIdConverter).ValueGeneratedNever();
        builder.Property(r => r.RecipeId).HasConversion(RecipeColumnConfiguration.RecipeIdConverter);
        builder.Property(r => r.ContentHash).HasMaxLength(ContentHashLength).IsFixedLength();

        builder.HasOne<Recipe>()
            .WithMany()
            .HasForeignKey(r => new { r.SiteId, r.RecipeId })
            .HasPrincipalKey(r => new { r.SiteId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // A recipe's revisions in order, one of each number; and its latest of a kind, a backwards scan of the second.
        builder.HasIndex(r => new { r.SiteId, r.RecipeId, r.Number }).IsUnique().HasDatabaseName(NumberIndexName);
        builder.HasIndex(r => new { r.SiteId, r.RecipeId, r.Kind, r.Number }).HasDatabaseName(KindIndexName);
    }
}
