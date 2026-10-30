using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// The ingredient lines table (PP4.3). A line refers to its group by <c>(SiteId, GroupId)</c> and goes with it, and to its
/// unit by code. The original text is Unicode and stored exactly; beneath the .NET rules the table checks that the parsed
/// parts fit the text and each other:
/// <list type="bullet">
/// <item>a line that could not be read has none of them, and one that could has an item;</item>
/// <item>a unit goes only with an amount or a range, and always with where it is in the text;</item>
/// <item>an amount or a range always says where it is in the text, and a span lies inside the text, apart from the
/// other.</item>
/// </list>
/// </summary>
internal sealed class RecipeIngredientConfiguration : IEntityTypeConfiguration<RecipeIngredient>
{
    public const string TableName = "RecipeIngredients";
    public const string QuantityPrefix = "Quantity";
    public const string PositionConstraintName = "CK_RecipeIngredients_Position";
    public const string OriginalTextConstraintName = "CK_RecipeIngredients_OriginalText";
    public const string ParseStatusConstraintName = "CK_RecipeIngredients_ParseStatus";
    public const string UnparsedConstraintName = "CK_RecipeIngredients_Unparsed";
    public const string ParsedConstraintName = "CK_RecipeIngredients_Parsed";
    public const string UnitConstraintName = "CK_RecipeIngredients_Unit";
    public const string QuantitySpanConstraintName = "CK_RecipeIngredients_QuantitySpan";
    public const string UnitSpanConstraintName = "CK_RecipeIngredients_UnitSpan";
    public const string SpansApartConstraintName = "CK_RecipeIngredients_SpansApart";

    private const string Kind = QuantityPrefix + "Kind";
    private const string QuantityStart = nameof(RecipeIngredient.QuantityStart);
    private const string QuantityLength = nameof(RecipeIngredient.QuantityLength);
    private const string UnitCodeColumn = nameof(RecipeIngredient.UnitCode);
    private const string UnitStart = nameof(RecipeIngredient.UnitStart);
    private const string UnitLength = nameof(RecipeIngredient.UnitLength);
    private const string Item = nameof(RecipeIngredient.Item);
    private const string Note = nameof(RecipeIngredient.Note);

    public void Configure(EntityTypeBuilder<RecipeIngredient> builder)
    {
        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(PositionConstraintName, RecipeColumnConfiguration.NonNegative(nameof(RecipeIngredient.Position)));
            table.HasCheckConstraint(OriginalTextConstraintName, RecipeColumnConfiguration.NotBlank(nameof(RecipeIngredient.OriginalText)));
            table.HasCheckConstraint(
                ParseStatusConstraintName,
                RecipeColumnConfiguration.InEnum<IngredientParseStatus>(nameof(RecipeIngredient.ParseStatus)));

            RecipeColumnConfiguration.Quantity(builder, table, TableName, QuantityPrefix, RecipeColumnConfiguration.AnyQuantity);

            var status = nameof(RecipeIngredient.ParseStatus);
            table.HasCheckConstraint(
                UnparsedConstraintName,
                $"[{status}] <> {(int)IngredientParseStatus.Unparsed} OR ([{Kind}] IS NULL AND [{UnitCodeColumn}] IS NULL "
                + $"AND [{Item}] IS NULL AND [{Note}] IS NULL AND [{QuantityStart}] IS NULL AND [{UnitStart}] IS NULL)");
            table.HasCheckConstraint(
                ParsedConstraintName,
                $"([{status}] <> {(int)IngredientParseStatus.Parsed} OR [{Item}] IS NOT NULL) "
                + $"AND ({RecipeColumnConfiguration.NotBlank(Item, nullable: true)}) AND ({RecipeColumnConfiguration.NotBlank(Note, nullable: true)})");

            // A check passes when it is unknown, so a null kind is ruled out in so many words.
            var measured = $"([{Kind}] IS NOT NULL AND [{Kind}] IN ({(int)QuantityKind.Amount}, {(int)QuantityKind.Range}))";
            table.HasCheckConstraint(UnitConstraintName, $"[{UnitCodeColumn}] IS NULL OR {measured}");

            table.HasCheckConstraint(
                QuantitySpanConstraintName,
                $"({Span(QuantityStart, QuantityLength)}) "
                + $"AND ([{QuantityStart}] IS NULL OR [{Kind}] IS NOT NULL) "
                + $"AND ([{QuantityStart}] IS NOT NULL OR [{Kind}] IS NULL OR [{Kind}] = {(int)QuantityKind.ToTaste})");
            table.HasCheckConstraint(
                UnitSpanConstraintName,
                $"({Span(UnitStart, UnitLength)}) "
                + $"AND (([{UnitCodeColumn}] IS NULL AND [{UnitStart}] IS NULL) OR ([{UnitCodeColumn}] IS NOT NULL AND [{UnitStart}] IS NOT NULL))");
            table.HasCheckConstraint(
                SpansApartConstraintName,
                $"[{QuantityStart}] IS NULL OR [{UnitStart}] IS NULL "
                + $"OR CAST([{QuantityStart}] AS bigint) + [{QuantityLength}] <= [{UnitStart}] "
                + $"OR CAST([{UnitStart}] AS bigint) + [{UnitLength}] <= [{QuantityStart}]");
        });

        builder.HasKey(i => new { i.SiteId, i.Id });
        builder.Property(i => i.Id).HasConversion(RecipeColumnConfiguration.IngredientIdConverter).ValueGeneratedNever();
        builder.Property(i => i.GroupId).HasConversion(RecipeColumnConfiguration.IngredientGroupIdConverter);
        builder.Ignore(i => i.Quantity);

        builder.Property(i => i.OriginalText).HasMaxLength(RecipeLimits.MaxIngredientTextLength);
        builder.Property(i => i.Item).HasMaxLength(RecipeLimits.MaxIngredientItemLength);
        builder.Property(i => i.Note).HasMaxLength(RecipeLimits.MaxIngredientNoteLength);

        builder.HasOne<RecipeIngredientGroup>()
            .WithMany()
            .HasForeignKey(i => new { i.SiteId, i.GroupId })
            .HasPrincipalKey(g => new { g.SiteId, g.Id })
            .OnDelete(DeleteBehavior.Cascade);
        RecipeColumnConfiguration.Vocabulary<RecipeIngredient, Unit>(builder, i => i.UnitCode!);

        // A group's lines in order. Not unique: reordering moves several at once, and their order is the facade's rule.
        builder.HasIndex(i => new { i.SiteId, i.GroupId, i.Position });
    }

    // Both ends or neither, and inside the text. DATALENGTH halves to UTF-16 code units, which is how .NET indexes a string;
    // LEN would count characters and ignore trailing spaces. Both are named not null, since a check that is unknown passes,
    // and the end is checked by subtraction, so a start near the largest int is refused by this check rather than an overflow.
    private static string Span(string start, string length)
    {
        var units = $"DATALENGTH([{nameof(RecipeIngredient.OriginalText)}]) / 2";
        return $"([{start}] IS NULL AND [{length}] IS NULL) OR ([{start}] IS NOT NULL AND [{length}] IS NOT NULL AND [{start}] >= 0 AND [{length}] > 0 "
            + $"AND [{start}] <= {units} AND [{length}] <= {units} - [{start}])";
    }
}
