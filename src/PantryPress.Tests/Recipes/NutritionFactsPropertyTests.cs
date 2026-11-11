using System.Globalization;
using System.Text.RegularExpressions;
using CsCheck;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.9: the laws nutrition keeps over every value its columns can hold (RCP-006): the facts are exactly the values the
// blogger entered - none added, none dropped, none changed - and each one's text reads back as its value.
public sealed partial class NutritionFactsPropertyTests
{
    private const long Iterations = 20_000;

    private static readonly Gen<long?> Value = Gen.Frequency(
        (2, Gen.Const((long?)null)),
        (1, Gen.OneOfConst<long?>(0, 1, 10, 100, NutritionFacts.MaxHundredths)),
        (3, Gen.Long[0, 200_000].Select(v => (long?)v)),
        (1, Gen.Long[0, NutritionFacts.MaxHundredths].Select(v => (long?)v)));

    private static readonly Gen<RecipeSnapshotNutrition> Nutrition = Gen.Select(
        Gen.Frequency((1, Gen.Const((string?)null)), (2, Gen.String.Select(s => (string?)s))),
        Value.Array[11],
        (serving, v) => new RecipeSnapshotNutrition(serving, v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10]));

    [Fact]
    public void The_facts_are_exactly_the_values_entered() =>
        Nutrition.Sample(iter: Iterations, assert: n =>
        {
            var facts = NutritionFacts.From(n);
            var entered = Entered(n);

            Assert.Same(n.ServingSize, facts.ServingSize);
            Assert.Equal(entered.Where(e => e.Value is not null).Select(e => (e.Nutrient, e.Value!.Value)), facts.Facts.Select(f => (f.Nutrient, f.Hundredths)));
            Assert.Equal(entered.Any(e => e.Value is not null), facts.HasNutrition);
        });

    // Each amount is ASCII digits with no leading zero, no trailing zero after a point, and no separator, and read back by
    // the framework's own decimal reader it is exactly the value entered.
    [Fact]
    public void Each_amount_reads_back_as_its_value() =>
        Nutrition.Sample(iter: Iterations, assert: n =>
        {
            foreach (var fact in NutritionFacts.From(n).Facts)
            {
                Assert.Matches(Amount(), fact.Amount);
                Assert.Equal(fact.Hundredths, decimal.ToInt64(decimal.Parse(fact.Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture) * 100m));
                Assert.Equal($"{fact.Amount} {fact.Unit}", fact.Text);
                Assert.Equal(NutritionFacts.UnitOf(fact.Nutrient), fact.Unit);
            }
        });

    [Fact]
    public void The_same_nutrition_gives_the_same_facts() =>
        Nutrition.Sample(iter: Iterations, assert: n =>
        {
            var first = NutritionFacts.From(n);
            var second = NutritionFacts.From(n with { });
            Assert.Equal(first, second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
        });

    private static (Nutrient Nutrient, long? Value)[] Entered(RecipeSnapshotNutrition n) =>
    [
        (Nutrient.Calories, n.Calories),
        (Nutrient.Fat, n.FatGrams),
        (Nutrient.SaturatedFat, n.SaturatedFatGrams),
        (Nutrient.TransFat, n.TransFatGrams),
        (Nutrient.UnsaturatedFat, n.UnsaturatedFatGrams),
        (Nutrient.Cholesterol, n.CholesterolMilligrams),
        (Nutrient.Sodium, n.SodiumMilligrams),
        (Nutrient.Carbohydrate, n.CarbohydrateGrams),
        (Nutrient.Fiber, n.FiberGrams),
        (Nutrient.Sugar, n.SugarGrams),
        (Nutrient.Protein, n.ProteinGrams),
    ];

    [GeneratedRegex(@"^(?:0|[1-9][0-9]{0,6})(?:\.[0-9]?[1-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex Amount();
}
