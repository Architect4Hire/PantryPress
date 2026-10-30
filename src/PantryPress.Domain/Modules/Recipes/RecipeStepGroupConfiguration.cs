using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// The step groups table (PP4.3). A group refers to its recipe by <c>(SiteId, RecipeId)</c>, so the database refuses a
/// group under another site's recipe, and goes with it when the recipe is deleted.
/// </summary>
internal sealed class RecipeStepGroupConfiguration : IEntityTypeConfiguration<RecipeStepGroup>
{
    public const string TableName = "RecipeStepGroups";
    public const string PositionConstraintName = "CK_RecipeStepGroups_Position";
    public const string HeadingConstraintName = "CK_RecipeStepGroups_Heading";

    public void Configure(EntityTypeBuilder<RecipeStepGroup> builder)
    {
        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(PositionConstraintName, RecipeColumnConfiguration.NonNegative(nameof(RecipeStepGroup.Position)));
            table.HasCheckConstraint(HeadingConstraintName, RecipeColumnConfiguration.NotBlank(nameof(RecipeStepGroup.Heading), nullable: true));
        });

        builder.HasKey(g => new { g.SiteId, g.Id });
        builder.Property(g => g.Id).HasConversion(RecipeColumnConfiguration.StepGroupIdConverter).ValueGeneratedNever();
        builder.Property(g => g.RecipeId).HasConversion(RecipeColumnConfiguration.RecipeIdConverter);
        builder.Property(g => g.Heading).HasMaxLength(RecipeLimits.MaxGroupHeadingLength);

        builder.HasOne<Recipe>()
            .WithMany()
            .HasForeignKey(g => new { g.SiteId, g.RecipeId })
            .HasPrincipalKey(r => new { r.SiteId, r.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // A recipe's step groups in order. Not unique: reordering moves several at once, and their order is the facade's rule.
        builder.HasIndex(g => new { g.SiteId, g.RecipeId, g.Position });
    }
}
