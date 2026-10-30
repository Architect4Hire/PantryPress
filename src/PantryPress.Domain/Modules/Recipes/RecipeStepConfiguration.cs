using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// The steps table (PP4.3). A step refers to its group by <c>(SiteId, GroupId)</c>, so the database refuses a step under
/// another site's group, and goes with it. Its text is Unicode and stored exactly.
/// </summary>
internal sealed class RecipeStepConfiguration : IEntityTypeConfiguration<RecipeStep>
{
    public const string TableName = "RecipeSteps";
    public const string PositionConstraintName = "CK_RecipeSteps_Position";
    public const string TextConstraintName = "CK_RecipeSteps_Text";

    public void Configure(EntityTypeBuilder<RecipeStep> builder)
    {
        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(PositionConstraintName, RecipeColumnConfiguration.NonNegative(nameof(RecipeStep.Position)));
            table.HasCheckConstraint(TextConstraintName, RecipeColumnConfiguration.NotBlank(nameof(RecipeStep.Text)));
        });

        builder.HasKey(s => new { s.SiteId, s.Id });
        builder.Property(s => s.Id).HasConversion(RecipeColumnConfiguration.StepIdConverter).ValueGeneratedNever();
        builder.Property(s => s.GroupId).HasConversion(RecipeColumnConfiguration.StepGroupIdConverter);
        builder.Property(s => s.Text).HasMaxLength(RecipeLimits.MaxStepTextLength);

        builder.HasOne<RecipeStepGroup>()
            .WithMany()
            .HasForeignKey(s => new { s.SiteId, s.GroupId })
            .HasPrincipalKey(g => new { g.SiteId, g.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // A group's steps in order. Not unique: reordering moves several at once, and their order is the facade's rule.
        builder.HasIndex(s => new { s.SiteId, s.GroupId, s.Position });
    }
}
