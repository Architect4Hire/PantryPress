using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// What the recipe tables share (PP4.3): the id converters, the text and position rules, a stored quantity's columns and
/// checks, and a reference to the platform's vocabulary (PP4.1).
/// </summary>
internal static class RecipeColumnConfiguration
{
    public static readonly ValueConverter<RecipeId, Guid> RecipeIdConverter = new(id => id.Value, value => new RecipeId(value));

    public static readonly ValueConverter<RecipeIngredientGroupId, Guid> IngredientGroupIdConverter =
        new(id => id.Value, value => new RecipeIngredientGroupId(value));

    public static readonly ValueConverter<RecipeIngredientId, Guid> IngredientIdConverter =
        new(id => id.Value, value => new RecipeIngredientId(value));

    public static readonly ValueConverter<RecipeStepGroupId, Guid> StepGroupIdConverter =
        new(id => id.Value, value => new RecipeStepGroupId(value));

    public static readonly ValueConverter<RecipeStepId, Guid> StepIdConverter = new(id => id.Value, value => new RecipeStepId(value));

    /// <summary>The kinds a stored quantity column may hold; a yield leaves out "to taste".</summary>
    public static readonly QuantityKind[] AnyQuantity = Enum.GetValues<QuantityKind>();

    /// <summary>
    /// Text that is not empty and not only spaces. <c>LEN</c> ignores trailing spaces, so a value of spaces alone fails.
    /// Nothing is trimmed: the check only refuses, and what is stored is exactly what was written (RCP-002).
    /// </summary>
    public static string NotBlank(string column, bool nullable = false) =>
        nullable ? $"[{column}] IS NULL OR LEN([{column}]) > 0" : $"LEN([{column}]) > 0";

    public static string InEnum<TEnum>(string column, IEnumerable<TEnum>? values = null)
        where TEnum : struct, Enum =>
        $"[{column}] IN ({string.Join(", ", (values ?? Enum.GetValues<TEnum>()).Select(v => Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture)))})";

    public static string NonNegative(string column) => $"[{column}] >= 0";

    /// <summary>
    /// Maps a stored quantity's five columns - <c>{prefix}Kind</c>, and the numerator and denominator of the low and high
    /// ends - to the entity's private fields, which only <c>StoredQuantity</c> reads and writes (EF Core names each
    /// property after its field, so a query names <c>_yieldKind</c>, not the column), and adds the checks the
    /// columns owe, named <c>CK_{table}_{prefix}…</c>:
    /// <list type="bullet">
    /// <item>the kind is one of <paramref name="kinds"/>, or none;</item>
    /// <item>the kind decides which ends are present: none for no quantity or "to taste", a low end for an amount, both
    /// for a range;</item>
    /// <item>every term is from 1 to <see cref="RecipeLimits.MaxQuantityTerm"/>, so an amount is above zero;</item>
    /// <item>a range's high end is above its low end, compared by cross-multiplying in <c>decimal(38,0)</c>, which no two
    /// 64-bit terms overflow.</item>
    /// </list>
    /// Lowest terms are .NET's to keep: reading a value back through <c>Rational.Create</c> reduces it anyway.
    /// </summary>
    public static void Quantity<T>(EntityTypeBuilder<T> builder, TableBuilder<T> table, string tableName, string prefix, QuantityKind[] kinds)
        where T : class
    {
        var field = "_" + char.ToLowerInvariant(prefix[0]) + prefix[1..];
        var (kind, lowN, lowD, highN, highD) =
            ($"{prefix}Kind", $"{prefix}LowNumerator", $"{prefix}LowDenominator", $"{prefix}HighNumerator", $"{prefix}HighDenominator");

        builder.Property<QuantityKind?>(field + "Kind").HasColumnName(kind);
        builder.Property<long?>(field + "LowNumerator").HasColumnName(lowN);
        builder.Property<long?>(field + "LowDenominator").HasColumnName(lowD);
        builder.Property<long?>(field + "HighNumerator").HasColumnName(highN);
        builder.Property<long?>(field + "HighDenominator").HasColumnName(highD);

        string AllNull(params string[] columns) => string.Join(" AND ", columns.Select(c => $"[{c}] IS NULL"));
        string AllSet(params string[] columns) => string.Join(" AND ", columns.Select(c => $"[{c}] IS NOT NULL"));

        table.HasCheckConstraint(QuantityConstraintName(tableName, prefix, "Kind"), $"[{kind}] IS NULL OR " + InEnum(kind, kinds));

        // A check passes when it is unknown, and a null kind compared with a number is unknown, so each branch names the kind
        // not null: otherwise a row with no kind but an amount's columns would be let in.
        var none = AllNull(lowN, lowD, highN, highD);
        string Is(QuantityKind k) => $"[{kind}] IS NOT NULL AND [{kind}] = {(int)k}";
        table.HasCheckConstraint(
            QuantityConstraintName(tableName, prefix, "Shape"),
            $"([{kind}] IS NULL AND {none})"
            + $" OR ({Is(QuantityKind.ToTaste)} AND {none})"
            + $" OR ({Is(QuantityKind.Amount)} AND {AllSet(lowN, lowD)} AND {AllNull(highN, highD)})"
            + $" OR ({Is(QuantityKind.Range)} AND {AllSet(lowN, lowD, highN, highD)})");

        table.HasCheckConstraint(
            QuantityConstraintName(tableName, prefix, "Terms"),
            string.Join(
                " AND ",
                new[] { lowN, lowD, highN, highD }.Select(c => $"([{c}] IS NULL OR [{c}] BETWEEN 1 AND {RecipeLimits.MaxQuantityTerm})")));

        table.HasCheckConstraint(
            QuantityConstraintName(tableName, prefix, "Range"),
            $"[{highN}] IS NULL OR CAST([{lowN}] AS decimal(38, 0)) * [{highD}] < CAST([{highN}] AS decimal(38, 0)) * [{lowD}]");
    }

    public static string QuantityConstraintName(string table, string prefix, string what) => $"CK_{table}_{prefix}{what}";

    /// <summary>
    /// A code column that refers to one of the vocabulary's tables: stored as the vocabulary stores it (ASCII, binary
    /// collation), so the foreign key matches byte for byte, and <c>Restrict</c>, though the vocabulary is never written by
    /// the app. No navigation: a tracked vocabulary row would meet the reference-data guard.
    /// <para>
    /// EF Core gives the code no index of its own, which would not lead with the site, and no read needs, since the
    /// vocabulary is never deleted from (<c>SiteForeignKeyIndexConvention</c>); <paramref name="covering"/>, led by the
    /// site, serves a read by code where one is owed (a library filtered by course, PP4.12).
    /// </para>
    /// </summary>
    public static void Vocabulary<TDependent, TPrincipal>(
        EntityTypeBuilder<TDependent> builder,
        Expression<Func<TDependent, string>> code,
        Expression<Func<TDependent, object?>>? covering = null)
        where TDependent : class
        where TPrincipal : class
    {
        VocabularyColumnConfiguration.Code(builder.Property(code));
        builder.HasOne<TPrincipal>()
            .WithMany()
            .HasForeignKey(Expression.Lambda<Func<TDependent, object?>>(Expression.Convert(code.Body, typeof(object)), code.Parameters))
            .OnDelete(DeleteBehavior.Restrict);

        if (covering is not null)
        {
            builder.HasIndex(covering);
        }
    }
}
