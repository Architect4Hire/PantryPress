using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Recipes.Business;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// The cuisines table (PP4.1), seeded from <see cref="Cuisines.All"/>: a change to the list changes the model, so it
/// arrives only through a reviewed migration.
/// </summary>
internal sealed class CuisineConfiguration : IEntityTypeConfiguration<Cuisine>
{
    public const string TableName = "Cuisines";
    public const string CodeShapeConstraintName = "CK_Cuisines_Code_Shape";

    public void Configure(EntityTypeBuilder<Cuisine> builder)
    {
        builder.ToTable(TableName, table => table.HasCheckConstraint(CodeShapeConstraintName, VocabularyColumnConfiguration.CodeShape()));

        builder.HasKey(w => w.Code);
        VocabularyColumnConfiguration.Code(builder.Property(w => w.Code));
        VocabularyColumnConfiguration.Name(builder.Property(w => w.Name));
        builder.HasIndex(w => w.Name).IsUnique();

        builder.HasData(Cuisines.All.Select(e => new Cuisine { Code = e.Code, Name = e.Name, Since = e.Since }));
    }
}
