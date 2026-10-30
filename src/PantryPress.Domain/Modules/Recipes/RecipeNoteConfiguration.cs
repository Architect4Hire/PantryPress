using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// The recipe notes table (PP4.3), keyed by <c>(SiteId, RecipeId, Position)</c>: the key refers to the recipe by site and id, so
/// the database refuses a row under another site's recipe, and the row goes with the recipe. Text is Unicode and stored
/// exactly.
/// </summary>
internal sealed class RecipeNoteConfiguration : IEntityTypeConfiguration<RecipeNote>
{
    public const string TableName = "RecipeNotes";
    public const string PositionConstraintName = "CK_RecipeNotes_Position";
    public const string TextConstraintName = "CK_RecipeNotes_Text";

    public void Configure(EntityTypeBuilder<RecipeNote> builder)
    {
        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(PositionConstraintName, RecipeColumnConfiguration.NonNegative(nameof(RecipeNote.Position)));
            table.HasCheckConstraint(TextConstraintName, RecipeColumnConfiguration.NotBlank(nameof(RecipeNote.Text)));
        });

        builder.HasKey(e => new { e.SiteId, e.RecipeId, e.Position });
        builder.Property(e => e.RecipeId).HasConversion(RecipeColumnConfiguration.RecipeIdConverter);
        builder.Property(e => e.Position).ValueGeneratedNever();
        builder.Property(e => e.Text).HasMaxLength(RecipeLimits.MaxNoteLength);

        builder.HasOne<Recipe>()
            .WithMany()
            .HasForeignKey(e => new { e.SiteId, e.RecipeId })
            .HasPrincipalKey(r => new { r.SiteId, r.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
