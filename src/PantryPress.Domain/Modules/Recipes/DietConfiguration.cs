using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Recipes.Business;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// The diets table (PP4.1), seeded from <see cref="Diets.All"/>: a change to the list changes the model, so it arrives
/// only through a reviewed migration.
/// </summary>
internal sealed class DietConfiguration : IEntityTypeConfiguration<Diet>
{
    public const string TableName = "Diets";
    public const string CodeShapeConstraintName = "CK_Diets_Code_Shape";
    public const string SchemaOrgDietConstraintName = "CK_Diets_SchemaOrgDiet";

    public void Configure(EntityTypeBuilder<Diet> builder)
    {
        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(CodeShapeConstraintName, VocabularyColumnConfiguration.CodeShape());

            // Built from the list, so a member added there changes the model and gets its migration.
            table.HasCheckConstraint(
                SchemaOrgDietConstraintName,
                "[SchemaOrgDiet] IS NULL OR [SchemaOrgDiet] IN (" +
                string.Join(", ", Diets.SchemaOrgMembers.Order(StringComparer.Ordinal).Select(m => $"'{m}'")) + ")");
        });

        builder.HasKey(d => d.Code);
        VocabularyColumnConfiguration.Code(builder.Property(d => d.Code));
        VocabularyColumnConfiguration.Name(builder.Property(d => d.Name));
        builder.HasIndex(d => d.Name).IsUnique();

        // Binary, as schema.org spells it: JSON-LD carries the member exactly, so the check compares it exactly too.
        builder.Property(d => d.SchemaOrgDiet)
            .HasMaxLength(Diets.SchemaOrgDietMaxLength)
            .IsUnicode(false)
            .UseCollation(VocabularyColumnConfiguration.BinaryCollation);

        builder.HasData(Diets.All.Select(e => new Diet
        {
            Code = e.Code,
            Name = e.Name,
            SchemaOrgDiet = e.SchemaOrgDiet,
            Since = e.Since,
        }));
    }
}
