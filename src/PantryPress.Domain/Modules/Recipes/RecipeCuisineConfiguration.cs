using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// The recipe cuisines table (PP4.3): an explicit site-owned join, keyed by <c>(SiteId, RecipeId, CuisineCode)</c>. The key refers to
/// the recipe by site and id, so the database refuses a row under another site's recipe, and the row goes with the recipe;
/// the code refers to the vocabulary's cuisines table. <c>(SiteId, CuisineCode)</c> serves finding a site's recipes by cuisine.
/// </summary>
internal sealed class RecipeCuisineConfiguration : IEntityTypeConfiguration<RecipeCuisine>
{
    public const string TableName = "RecipeCuisines";
    public const string PositionConstraintName = "CK_RecipeCuisines_Position";

    public void Configure(EntityTypeBuilder<RecipeCuisine> builder)
    {
        builder.ToTable(
            TableName,
            table => table.HasCheckConstraint(PositionConstraintName, RecipeColumnConfiguration.NonNegative(nameof(RecipeCuisine.Position))));

        builder.HasKey(e => new { e.SiteId, e.RecipeId, e.CuisineCode });
        builder.Property(e => e.RecipeId).HasConversion(RecipeColumnConfiguration.RecipeIdConverter);

        builder.HasOne<Recipe>()
            .WithMany()
            .HasForeignKey(e => new { e.SiteId, e.RecipeId })
            .HasPrincipalKey(r => new { r.SiteId, r.Id })
            .OnDelete(DeleteBehavior.Cascade);
        RecipeColumnConfiguration.Vocabulary<RecipeCuisine, Cuisine>(builder, e => e.CuisineCode, e => new { e.SiteId, e.CuisineCode });
    }
}
