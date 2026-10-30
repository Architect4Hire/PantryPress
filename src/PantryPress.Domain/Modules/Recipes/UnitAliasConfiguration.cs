using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Recipes.Business;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// The unit aliases table (PP4.1), seeded from <see cref="Units.Aliases"/>: a change to the list changes the model, so it
/// arrives only through a reviewed migration.
/// </summary>
internal sealed class UnitAliasConfiguration : IEntityTypeConfiguration<UnitAlias>
{
    public const string TableName = "UnitAliases";

    /// <summary>A case-insensitive alias is stored lower-case, so one spelling stands for every case of it.</summary>
    public const string CaseConstraintName = "CK_UnitAliases_Case";

    /// <summary>An alias is not empty and has no space at either end, which equality in SQL Server would ignore.</summary>
    public const string TrimmedConstraintName = "CK_UnitAliases_Trimmed";

    private static readonly ValueComparer<string> OrdinalComparer = new(
        (a, b) => string.Equals(a, b, StringComparison.Ordinal),
        v => StringComparer.Ordinal.GetHashCode(v));

    public void Configure(EntityTypeBuilder<UnitAlias> builder)
    {
        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                CaseConstraintName,
                $"[CaseSensitive] = 1 OR [Alias] = LOWER([Alias]) COLLATE {VocabularyColumnConfiguration.BinaryCollation}");
            table.HasCheckConstraint(
                TrimmedConstraintName,
                "LEN([Alias]) > 0 AND DATALENGTH([Alias]) = DATALENGTH(LTRIM(RTRIM([Alias])))");
        });

        // Unicode, for the degree sign; binary, so T and t are two keys - in the database and, by the ordinal comparer,
        // in EF Core too, which would otherwise take them for one seed row.
        builder.HasKey(a => a.Alias);
        builder.Property(a => a.Alias)
            .HasMaxLength(Units.AliasMaxLength)
            .UseCollation(VocabularyColumnConfiguration.BinaryCollation)
            .Metadata.SetValueComparer(OrdinalComparer);

        VocabularyColumnConfiguration.Code(builder.Property(a => a.UnitCode));
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(a => a.UnitCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(Units.Aliases.Select(e => new UnitAlias
        {
            Alias = e.Alias,
            UnitCode = e.UnitCode,
            CaseSensitive = e.CaseSensitive,
            Since = e.Since,
        }));
    }
}
