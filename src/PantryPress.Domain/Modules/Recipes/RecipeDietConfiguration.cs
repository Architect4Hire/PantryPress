using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// The recipe diets table (PP4.3): an explicit site-owned join, keyed by <c>(SiteId, RecipeId, DietCode)</c>. The key refers to
/// the recipe by site and id, so the database refuses a row under another site's recipe, and the row goes with the recipe;
/// the code refers to the vocabulary's diets table. <c>(SiteId, DietCode)</c> serves finding a site's recipes by diet.
/// </summary>
internal sealed class RecipeDietConfiguration : IEntityTypeConfiguration<RecipeDiet>
{
    public const string TableName = "RecipeDiets";
    public const string PositionConstraintName = "CK_RecipeDiets_Position";

    public void Configure(EntityTypeBuilder<RecipeDiet> builder)
    {
        builder.ToTable(
            TableName,
            table => table.HasCheckConstraint(PositionConstraintName, RecipeColumnConfiguration.NonNegative(nameof(RecipeDiet.Position))));

        builder.HasKey(e => new { e.SiteId, e.RecipeId, e.DietCode });
        builder.Property(e => e.RecipeId).HasConversion(RecipeColumnConfiguration.RecipeIdConverter);

        builder.HasOne<Recipe>()
            .WithMany()
            .HasForeignKey(e => new { e.SiteId, e.RecipeId })
            .HasPrincipalKey(r => new { r.SiteId, r.Id })
            .OnDelete(DeleteBehavior.Cascade);
        RecipeColumnConfiguration.Vocabulary<RecipeDiet, Diet>(builder, e => e.DietCode, e => new { e.SiteId, e.DietCode });
    }
}
