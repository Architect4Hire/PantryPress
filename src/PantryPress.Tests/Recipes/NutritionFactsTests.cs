using System.Globalization;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.9: nutrition per serving exactly as the blogger entered it (RCP-006, RND-004). Nothing is estimated, derived, rounded,
// or totalled. Every expectation is worked by hand.
public sealed class NutritionFactsTests
{
    private static readonly RecipeSnapshotNutrition Nothing = new(null, null, null, null, null, null, null, null, null, null, null, null);

    [Fact]
    public void Only_what_was_entered_is_listed_in_label_order()
    {
        // The post mockup's banana bread, entered in hundredths: 312 kcal, 14 g fat, 48 mg cholesterol, 260 mg sodium, 43 g
        // carbohydrate, 2 g fiber, 22 g sugar, 5 g protein; nothing for saturated, trans, or unsaturated fat.
        var facts = NutritionFacts.From(Nothing with
        {
            ServingSize = "1 slice",
            Calories = 31_200,
            FatGrams = 1_400,
            CholesterolMilligrams = 4_800,
            SodiumMilligrams = 26_000,
            CarbohydrateGrams = 4_300,
            FiberGrams = 200,
            SugarGrams = 2_200,
            ProteinGrams = 500,
        });

        Assert.True(facts.HasNutrition);
        Assert.Equal("1 slice", facts.ServingSize);
        Assert.Equal(
            ["312 kcal", "14 g", "48 mg", "260 mg", "43 g", "2 g", "22 g", "5 g"],
            facts.Facts.Select(f => f.Text));
        Assert.Equal(
            [Nutrient.Calories, Nutrient.Fat, Nutrient.Cholesterol, Nutrient.Sodium, Nutrient.Carbohydrate, Nutrient.Fiber, Nutrient.Sugar, Nutrient.Protein],
            facts.Facts.Select(f => f.Nutrient));
    }

    // Each column reaches its own nutrient: every value is different, so a swapped pair would show.
    [Fact]
    public void Each_value_is_its_own_nutrients()
    {
        var facts = NutritionFacts.From(new RecipeSnapshotNutrition("1 bowl", 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11));

        Assert.Equal(
            [
                (Nutrient.Calories, 1L, "kcal"),
                (Nutrient.Fat, 2L, "g"),
                (Nutrient.SaturatedFat, 3L, "g"),
                (Nutrient.TransFat, 4L, "g"),
                (Nutrient.UnsaturatedFat, 5L, "g"),
                (Nutrient.Cholesterol, 6L, "mg"),
                (Nutrient.Sodium, 7L, "mg"),
                (Nutrient.Carbohydrate, 8L, "g"),
                (Nutrient.Fiber, 9L, "g"),
                (Nutrient.Sugar, 10L, "g"),
                (Nutrient.Protein, 11L, "g"),
            ],
            facts.Facts.Select(f => (f.Nutrient, f.Hundredths, f.Unit)));
    }

    // Zero is a value the blogger entered; an empty one is not.
    [Fact]
    public void Zero_is_shown_and_empty_is_not()
    {
        var facts = NutritionFacts.From(Nothing with { FatGrams = 900, TransFatGrams = 0 });

        Assert.Equal(["9 g", "0 g"], facts.Facts.Select(f => f.Text));
        Assert.Equal([Nutrient.Fat, Nutrient.TransFat], facts.Facts.Select(f => f.Nutrient));
    }

    // Saturated and trans fat are entered and total fat is not: total fat is not worked out from them, and unsaturated fat
    // is not worked out from total fat. Calories are not worked out from the macronutrients.
    [Fact]
    public void Nothing_is_derived_from_the_other_values()
    {
        var fats = NutritionFacts.From(Nothing with { SaturatedFatGrams = 300, TransFatGrams = 50 });
        Assert.Equal([Nutrient.SaturatedFat, Nutrient.TransFat], fats.Facts.Select(f => f.Nutrient));

        var macros = NutritionFacts.From(Nothing with { FatGrams = 1_000, CarbohydrateGrams = 2_000, ProteinGrams = 500 });
        Assert.Equal([Nutrient.Fat, Nutrient.Carbohydrate, Nutrient.Protein], macros.Facts.Select(f => f.Nutrient));
    }

    [Fact]
    public void A_serving_size_with_no_values_is_no_panel()
    {
        var facts = NutritionFacts.From(Nothing with { ServingSize = "1 slice" });

        Assert.False(facts.HasNutrition);
        Assert.Empty(facts.Facts);
        Assert.Equal("1 slice", facts.ServingSize);
    }

    [Fact]
    public void Nothing_entered_is_no_panel()
    {
        var facts = NutritionFacts.From(Nothing);

        Assert.False(facts.HasNutrition);
        Assert.Empty(facts.Facts);
        Assert.Null(facts.ServingSize);
    }

    [Fact]
    public void Values_without_a_serving_size_are_a_panel()
    {
        var facts = NutritionFacts.From(Nothing with { Calories = 25_000 });

        Assert.True(facts.HasNutrition);
        Assert.Null(facts.ServingSize);
        Assert.Equal(["250 kcal"], facts.Facts.Select(f => f.Text));
    }

    // The blogger's words for the serving are passed on exactly, spaces and all.
    [Theory]
    [InlineData("1 slice")]
    [InlineData(" ½ cup (about 120 g) ")]
    [InlineData("1 Stück")]
    public void The_serving_size_is_the_bloggers_own_words(string servingSize) =>
        Assert.Same(servingSize, NutritionFacts.From(Nothing with { ServingSize = servingSize, Calories = 100 }).ServingSize);

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "0.01")]
    [InlineData(9, "0.09")]
    [InlineData(10, "0.1")]
    [InlineData(50, "0.5")]
    [InlineData(99, "0.99")]
    [InlineData(100, "1")]
    [InlineData(105, "1.05")]
    [InlineData(110, "1.1")]
    [InlineData(1_400, "14")]
    [InlineData(1_450, "14.5")]
    [InlineData(1_425, "14.25")]
    [InlineData(31_200, "312")]
    [InlineData(230_000, "2300")]
    [InlineData(100_000_000, "1000000")]
    [InlineData(NutritionFacts.MaxHundredths, "9999999.99")]
    public void An_amount_is_written_exactly_with_no_trailing_zeros(long hundredths, string amount) =>
        Assert.Equal(amount, NutritionFacts.FormatAmount(hundredths));

    [Theory]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    [InlineData(NutritionFacts.MaxHundredths + 1)]
    [InlineData(long.MaxValue)]
    public void A_value_the_column_cannot_hold_is_a_bug(long hundredths)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NutritionFacts.FormatAmount(hundredths));
        Assert.Throws<ArgumentOutOfRangeException>(() => NutritionFacts.From(Nothing with { SodiumMilligrams = hundredths }));
    }

    [Fact]
    public void The_largest_value_is_the_columns()
    {
        Assert.Equal(2, RecipeLimits.NutritionScale);
        var largest = 1L;
        for (var i = 0; i < RecipeLimits.NutritionPrecision; i++)
        {
            largest *= 10;
        }

        Assert.Equal(NutritionFacts.MaxHundredths, largest - 1);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("fr-FR")]
    [InlineData("hi-IN")]
    [InlineData("")]
    public void Facts_read_alike_in_every_culture(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var facts = NutritionFacts.From(Nothing with { Calories = 31_250, SodiumMilligrams = 230_000, FiberGrams = 5 });
            Assert.Equal(["312.5 kcal", "2300 mg", "0.05 g"], facts.Facts.Select(f => f.Text));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Not_a_nutrient_has_no_unit() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NutritionFacts.UnitOf((Nutrient)Enum.GetValues<Nutrient>().Length));

    // Only what was entered, in label order: nothing outside the type can make one or change its facts, and a fact is made
    // only from a nutrient and its stored value.
    [Fact]
    public void Facts_are_made_only_from_stored_nutrition()
    {
        Assert.Empty(typeof(NutritionFacts).GetConstructors());
        Assert.All(typeof(NutritionFacts).GetProperties(), p => Assert.Null(p.SetMethod));
        Assert.Equal(
            [[typeof(Nutrient), typeof(long)]],
            typeof(NutritionFact).GetConstructors().Select(c => c.GetParameters().Select(p => p.ParameterType).ToArray()));
        Assert.All(typeof(NutritionFact).GetProperties(), p => Assert.Null(p.SetMethod));
    }

    // Two readings of the same nutrition are equal, facts compared in order; a different value, a missing one, or a
    // different serving size is not.
    [Fact]
    public void Facts_are_equal_by_value()
    {
        var a = NutritionFacts.From(Nothing with { ServingSize = "1 slice", Calories = 100, ProteinGrams = 0 });
        var b = NutritionFacts.From(Nothing with { ServingSize = "1 slice", Calories = 100, ProteinGrams = 0 });

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a == b);
        Assert.NotEqual(a, NutritionFacts.From(Nothing with { ServingSize = "1 slice", Calories = 100, ProteinGrams = 1 }));
        Assert.NotEqual(a, NutritionFacts.From(Nothing with { ServingSize = "1 slice", Calories = 100 }));
        Assert.NotEqual(a, NutritionFacts.From(Nothing with { ServingSize = "1 Slice", Calories = 100, ProteinGrams = 0 }));
        Assert.NotEqual(a, NutritionFacts.From(Nothing with { Calories = 100, ProteinGrams = 0 }));
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void A_null_nutrition_is_a_bug() =>
        Assert.Throws<ArgumentNullException>(() => NutritionFacts.From(null!));

    // Page data will carry the nutrient as kebab-case (PP7.11), so its members and their order are pinned.
    [Fact]
    public void The_nutrients_are_pinned_in_label_order() =>
        Assert.Equal(
            ["Calories", "Fat", "SaturatedFat", "TransFat", "UnsaturatedFat", "Cholesterol", "Sodium", "Carbohydrate", "Fiber", "Sugar", "Protein"],
            Enum.GetNames<Nutrient>());

    // The snapshot's nutrition columns, in declaration order, are the nutrients in theirs, each named for its nutrient and
    // its unit, so a nutrient added to one and not the other, or a unit that disagrees with its column, fails here.
    [Fact]
    public void Every_nutrient_is_a_snapshot_column_in_its_unit()
    {
        var columns = typeof(RecipeSnapshotNutrition).GetProperties()
            .Where(p => p.Name != nameof(RecipeSnapshotNutrition.ServingSize))
            .OrderBy(p => p.MetadataToken)
            .Select(p => p.Name)
            .ToArray();

        var expected = Enum.GetValues<Nutrient>().Select(n => n.ToString() + NutritionFacts.UnitOf(n) switch
        {
            "kcal" => string.Empty,
            "g" => "Grams",
            "mg" => "Milligrams",
            var unit => throw new InvalidOperationException(unit),
        });
        Assert.Equal(expected, columns);
    }
}
