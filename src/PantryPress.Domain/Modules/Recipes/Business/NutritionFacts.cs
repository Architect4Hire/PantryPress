using System.Globalization;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// A recipe's nutrition per serving as a reader sees it (RCP-006, RND-004, CMP-005; PP4.9): exactly what the blogger entered,
/// and nothing else. PantryPress never estimates nutrition, so nothing here is worked out:
/// <list type="bullet">
/// <item>Only the nutrients the blogger entered are listed, in <see cref="Nutrient"/>'s order. An empty value is left out,
/// and zero is a value the blogger entered, so <c>0 g</c> is shown.</item>
/// <item>No value is derived from others: no calories from the macronutrients, no unsaturated fat from total fat.</item>
/// <item>Nothing is rounded - not to a label's rules, not at all - and nothing is totalled for the whole recipe.</item>
/// <item>Nothing takes a yield or a scale: nutrition is per serving, so every scaled variant shows the same facts.</item>
/// </list>
/// The panel is shown only when <see cref="HasNutrition"/>, and always with the disclaimer, which is the recipe card's copy
/// (PP7.7). Each value is held as whole hundredths, the two places its column keeps (PP4.4), so no floating point is needed.
/// </summary>
public sealed record NutritionFacts
{
    private NutritionFacts(string? servingSize, IReadOnlyList<NutritionFact> facts)
    {
        ServingSize = servingSize;
        Facts = facts;
    }

    /// <summary>The serving the values are for, in the blogger's own words, unchanged.</summary>
    public string? ServingSize { get; }

    /// <summary>The values entered, in <see cref="Nutrient"/>'s order. <see cref="From"/> is the only way to make one.</summary>
    public IReadOnlyList<NutritionFact> Facts { get; }

    /// <summary>
    /// The largest value a nutrient's <c>decimal(9,2)</c> column holds (9,999,999.99), in hundredths:
    /// 10 to the <see cref="RecipeLimits.NutritionPrecision"/>, less one.
    /// </summary>
    public const long MaxHundredths = 999_999_999;

    /// <summary>Whether the blogger entered any value. A serving size with no values is no panel.</summary>
    public bool HasNutrition => Facts.Count > 0;

    /// <summary>
    /// The facts from a recipe's stored nutrition, values in hundredths. The serving size is the blogger's own words,
    /// unchanged. A value below zero or past <see cref="MaxHundredths"/> cannot be stored, so it is a bug, and throws.
    /// </summary>
    internal static NutritionFacts From(RecipeSnapshotNutrition nutrition)
    {
        ArgumentNullException.ThrowIfNull(nutrition);
        (Nutrient Nutrient, long? Hundredths)[] entered =
        [
            (Nutrient.Calories, nutrition.Calories),
            (Nutrient.Fat, nutrition.FatGrams),
            (Nutrient.SaturatedFat, nutrition.SaturatedFatGrams),
            (Nutrient.TransFat, nutrition.TransFatGrams),
            (Nutrient.UnsaturatedFat, nutrition.UnsaturatedFatGrams),
            (Nutrient.Cholesterol, nutrition.CholesterolMilligrams),
            (Nutrient.Sodium, nutrition.SodiumMilligrams),
            (Nutrient.Carbohydrate, nutrition.CarbohydrateGrams),
            (Nutrient.Fiber, nutrition.FiberGrams),
            (Nutrient.Sugar, nutrition.SugarGrams),
            (Nutrient.Protein, nutrition.ProteinGrams),
        ];

        var facts = new List<NutritionFact>(entered.Length);
        foreach (var (nutrient, hundredths) in entered)
        {
            if (hundredths is { } value)
            {
                facts.Add(new NutritionFact(nutrient, value));
            }
        }

        return new NutritionFacts(nutrition.ServingSize, facts.AsReadOnly());
    }

    /// <summary>Equal when the serving size and every fact, in order, are equal.</summary>
    public bool Equals(NutritionFacts? other) =>
        other is not null && string.Equals(ServingSize, other.ServingSize, StringComparison.Ordinal) && Facts.SequenceEqual(other.Facts);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ServingSize, StringComparer.Ordinal);
        foreach (var fact in Facts)
        {
            hash.Add(fact);
        }

        return hash.ToHashCode();
    }

    /// <summary>The unit every value of a nutrient is entered in (PP4.3's columns), as the recipe card writes it.</summary>
    public static string UnitOf(Nutrient nutrient) => nutrient switch
    {
        Nutrient.Calories => "kcal",
        Nutrient.Cholesterol or Nutrient.Sodium => "mg",
        Nutrient.Fat or Nutrient.SaturatedFat or Nutrient.TransFat or Nutrient.UnsaturatedFat
            or Nutrient.Carbohydrate or Nutrient.Fiber or Nutrient.Sugar or Nutrient.Protein => "g",
        _ => throw new ArgumentOutOfRangeException(nameof(nutrient), nutrient, "Not a nutrient."),
    };

    /// <summary>
    /// A value in hundredths written exactly, with ASCII digits and <c>.</c> and no culture: trailing zeros after the point
    /// are left off, and so is a point with nothing after it (<c>14</c>, <c>14.5</c>, <c>14.25</c>, <c>0</c>). There is no
    /// thousands separator (<c>2300</c>; owner decision, PP4.9).
    /// </summary>
    public static string FormatAmount(long hundredths)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hundredths);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hundredths, MaxHundredths);

        var whole = (hundredths / 100).ToString(CultureInfo.InvariantCulture);
        var cents = hundredths % 100;
        return cents == 0 ? whole
            : cents % 10 == 0 ? $"{whole}.{(cents / 10).ToString(CultureInfo.InvariantCulture)}"
            : $"{whole}.{cents.ToString("00", CultureInfo.InvariantCulture)}";
    }
}

/// <summary>One nutrient the blogger entered, per serving, exactly as entered (RCP-006; PP4.9).</summary>
public sealed record NutritionFact
{
    public NutritionFact(Nutrient nutrient, long hundredths)
    {
        Amount = NutritionFacts.FormatAmount(hundredths);
        Unit = NutritionFacts.UnitOf(nutrient);
        Nutrient = nutrient;
        Hundredths = hundredths;
    }

    public Nutrient Nutrient { get; }

    /// <summary>The value in hundredths of its unit, as stored.</summary>
    public long Hundredths { get; }

    /// <summary>The value's text: <c>312</c>, <c>14.5</c>, <c>0</c>.</summary>
    public string Amount { get; }

    /// <summary><c>kcal</c>, <c>g</c>, or <c>mg</c>.</summary>
    public string Unit { get; }

    /// <summary>The amount and its unit, as the recipe card shows them: <c>312 kcal</c>, <c>14.5 g</c>, <c>260 mg</c>.</summary>
    public string Text => $"{Amount} {Unit}";
}
