using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// The recipe courses table (PP4.3): an explicit site-owned join, keyed by <c>(SiteId, RecipeId, CourseCode)</c>. The key refers to
/// the recipe by site and id, so the database refuses a row under another site's recipe, and the row goes with the recipe;
/// the code refers to the vocabulary's courses table. <c>(SiteId, CourseCode)</c> serves finding a site's recipes by course.
/// </summary>
internal sealed class RecipeCourseConfiguration : IEntityTypeConfiguration<RecipeCourse>
{
    public const string TableName = "RecipeCourses";
    public const string PositionConstraintName = "CK_RecipeCourses_Position";

    public void Configure(EntityTypeBuilder<RecipeCourse> builder)
    {
        builder.ToTable(
            TableName,
            table => table.HasCheckConstraint(PositionConstraintName, RecipeColumnConfiguration.NonNegative(nameof(RecipeCourse.Position))));

        builder.HasKey(e => new { e.SiteId, e.RecipeId, e.CourseCode });
        builder.Property(e => e.RecipeId).HasConversion(RecipeColumnConfiguration.RecipeIdConverter);

        builder.HasOne<Recipe>()
            .WithMany()
            .HasForeignKey(e => new { e.SiteId, e.RecipeId })
            .HasPrincipalKey(r => new { r.SiteId, r.Id })
            .OnDelete(DeleteBehavior.Cascade);
        RecipeColumnConfiguration.Vocabulary<RecipeCourse, Course>(builder, e => e.CourseCode, e => new { e.SiteId, e.CourseCode });
    }
}
