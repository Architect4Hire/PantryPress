using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// The units table (PP4.1), seeded from <see cref="Units.All"/>: a change to the list changes the model, so it arrives
/// only through a reviewed migration.
/// </summary>
internal sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public const string TableName = "Units";
    public const string CodeShapeConstraintName = "CK_Units_Code_Shape";
    public const string SystemConstraintName = "CK_Units_System";
    public const string DimensionConstraintName = "CK_Units_Dimension";

    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(CodeShapeConstraintName, VocabularyColumnConfiguration.CodeShape());

            // Built from the enums, so a new system or dimension changes the model and gets its migration.
            table.HasCheckConstraint(
                SystemConstraintName,
                $"[System] IN ({string.Join(", ", Enum.GetValues<UnitSystem>().Select(s => (int)s))})");
            table.HasCheckConstraint(
                DimensionConstraintName,
                $"[Dimension] IN ({string.Join(", ", Enum.GetValues<UnitDimension>().Select(d => (int)d))})");
        });

        builder.HasKey(u => u.Code);
        VocabularyColumnConfiguration.Code(builder.Property(u => u.Code));
        builder.Property(u => u.Singular).HasMaxLength(Units.WordMaxLength);
        builder.Property(u => u.Plural).HasMaxLength(Units.WordMaxLength);
        builder.Property(u => u.Abbreviation).HasMaxLength(Units.AbbreviationMaxLength);

        builder.HasData(Units.All.Select(e => new Unit
        {
            Code = e.Code,
            System = e.System,
            Dimension = e.Dimension,
            Singular = e.Singular,
            Plural = e.Plural,
            Abbreviation = e.Abbreviation,
            Since = e.Since,
        }));
    }
}
