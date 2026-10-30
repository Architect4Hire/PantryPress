using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Recipes.Business;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// The allergens table (PP4.1), seeded from <see cref="Allergens.All"/>: a change to the list changes the model, so it
/// arrives only through a reviewed migration.
/// </summary>
internal sealed class AllergenConfiguration : IEntityTypeConfiguration<Allergen>
{
    public const string TableName = "Allergens";
    public const string CodeShapeConstraintName = "CK_Allergens_Code_Shape";

    public void Configure(EntityTypeBuilder<Allergen> builder)
    {
        builder.ToTable(TableName, table => table.HasCheckConstraint(CodeShapeConstraintName, VocabularyColumnConfiguration.CodeShape()));

        builder.HasKey(w => w.Code);
        VocabularyColumnConfiguration.Code(builder.Property(w => w.Code));
        VocabularyColumnConfiguration.Name(builder.Property(w => w.Name));
        builder.HasIndex(w => w.Name).IsUnique();

        builder.HasData(Allergens.All.Select(e => new Allergen { Code = e.Code, Name = e.Name, Since = e.Since }));
    }
}
