using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// The recipe allergens table (PP4.3): an explicit site-owned join, keyed by <c>(SiteId, RecipeId, AllergenCode)</c>. The key refers to
/// the recipe by site and id, so the database refuses a row under another site's recipe, and the row goes with the recipe;
/// the code refers to the vocabulary's allergens table. <c>(SiteId, AllergenCode)</c> serves finding a site's recipes by allergen.
/// </summary>
internal sealed class RecipeAllergenConfiguration : IEntityTypeConfiguration<RecipeAllergen>
{
    public const string TableName = "RecipeAllergens";
    public const string PositionConstraintName = "CK_RecipeAllergens_Position";

    public void Configure(EntityTypeBuilder<RecipeAllergen> builder)
    {
        builder.ToTable(
            TableName,
            table => table.HasCheckConstraint(PositionConstraintName, RecipeColumnConfiguration.NonNegative(nameof(RecipeAllergen.Position))));

        builder.HasKey(e => new { e.SiteId, e.RecipeId, e.AllergenCode });
        builder.Property(e => e.RecipeId).HasConversion(RecipeColumnConfiguration.RecipeIdConverter);

        builder.HasOne<Recipe>()
            .WithMany()
            .HasForeignKey(e => new { e.SiteId, e.RecipeId })
            .HasPrincipalKey(r => new { r.SiteId, r.Id })
            .OnDelete(DeleteBehavior.Cascade);
        RecipeColumnConfiguration.Vocabulary<RecipeAllergen, Allergen>(builder, e => e.AllergenCode, e => new { e.SiteId, e.AllergenCode });
    }
}
