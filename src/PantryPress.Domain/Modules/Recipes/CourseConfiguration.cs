using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Recipes.Business;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// The courses table (PP4.1), seeded from <see cref="Courses.All"/>: a change to the list changes the model, so it
/// arrives only through a reviewed migration.
/// </summary>
internal sealed class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public const string TableName = "Courses";
    public const string CodeShapeConstraintName = "CK_Courses_Code_Shape";

    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.ToTable(TableName, table => table.HasCheckConstraint(CodeShapeConstraintName, VocabularyColumnConfiguration.CodeShape()));

        builder.HasKey(w => w.Code);
        VocabularyColumnConfiguration.Code(builder.Property(w => w.Code));
        VocabularyColumnConfiguration.Name(builder.Property(w => w.Name));
        builder.HasIndex(w => w.Name).IsUnique();

        builder.HasData(Courses.All.Select(e => new Course { Code = e.Code, Name = e.Name, Since = e.Since }));
    }
}
