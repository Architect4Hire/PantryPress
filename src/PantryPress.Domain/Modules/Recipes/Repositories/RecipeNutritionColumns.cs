using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Repositories;

/// <summary>
/// A recipe's nutrition between its eleven <c>decimal(9,2)</c> columns and what reads or writes them (RCP-006; PP4.4,
/// PP4.11): the snapshot's hundredths and the API's amounts. Here, beside the columns, because the Recipes Business code
/// never names a fractional number type (PP-D05). Every value goes across exactly as entered; nothing is rounded, derived,
/// or estimated. It holds no EF Core, but it is about the columns, and the repository reads through it, so it sits beside
/// them; the data layer passes it up (the written exception, PP4.11).
/// </summary>
internal static class RecipeNutritionColumns
{
    /// <summary>Nutrition in hundredths, which is what its two-place columns hold.</summary>
    public static RecipeSnapshotNutrition Hundredths(Recipe recipe) => new(
        recipe.ServingSize,
        Hundredths(recipe.Calories),
        Hundredths(recipe.FatGrams),
        Hundredths(recipe.SaturatedFatGrams),
        Hundredths(recipe.TransFatGrams),
        Hundredths(recipe.UnsaturatedFatGrams),
        Hundredths(recipe.CholesterolMilligrams),
        Hundredths(recipe.SodiumMilligrams),
        Hundredths(recipe.CarbohydrateGrams),
        Hundredths(recipe.FiberGrams),
        Hundredths(recipe.SugarGrams),
        Hundredths(recipe.ProteinGrams));

    /// <summary>
    /// The recipe's nutrition as the API answers it, or none when nothing is entered. Each amount is written in its
    /// shortest form (<c>410.5</c>, never the column's <c>410.50</c>), so an amount reads the same whether it came from the
    /// columns or from what was just saved.
    /// </summary>
    public static RecipeNutrition? Read(Recipe recipe)
    {
        var nutrition = new RecipeNutrition(
            recipe.ServingSize,
            Shortest(recipe.Calories),
            Shortest(recipe.FatGrams),
            Shortest(recipe.SaturatedFatGrams),
            Shortest(recipe.TransFatGrams),
            Shortest(recipe.UnsaturatedFatGrams),
            Shortest(recipe.CholesterolMilligrams),
            Shortest(recipe.SodiumMilligrams),
            Shortest(recipe.CarbohydrateGrams),
            Shortest(recipe.FiberGrams),
            Shortest(recipe.SugarGrams),
            Shortest(recipe.ProteinGrams));
        return nutrition == Empty ? null : nutrition;
    }

    /// <summary>
    /// Writes <paramref name="nutrition"/> to the recipe's columns, clearing every one it leaves out. Each amount was
    /// checked to have at most two decimal places, so the column holds it exactly.
    /// </summary>
    public static void Write(Recipe recipe, RecipeNutrition? nutrition)
    {
        var n = nutrition ?? Empty;
        recipe.ServingSize = n.ServingSize;
        recipe.Calories = n.Calories;
        recipe.FatGrams = n.FatGrams;
        recipe.SaturatedFatGrams = n.SaturatedFatGrams;
        recipe.TransFatGrams = n.TransFatGrams;
        recipe.UnsaturatedFatGrams = n.UnsaturatedFatGrams;
        recipe.CholesterolMilligrams = n.CholesterolMilligrams;
        recipe.SodiumMilligrams = n.SodiumMilligrams;
        recipe.CarbohydrateGrams = n.CarbohydrateGrams;
        recipe.FiberGrams = n.FiberGrams;
        recipe.SugarGrams = n.SugarGrams;
        recipe.ProteinGrams = n.ProteinGrams;
    }

    private static readonly RecipeNutrition Empty = new(null, null, null, null, null, null, null, null, null, null, null, null);

    // The same value without trailing zeros after its point: dividing by one written with the most places a decimal holds
    // drops them, and changes nothing else.
    private static decimal? Shortest(decimal? value) => value is { } v ? v / 1.0000000000000000000000000000m : null;

    // Exact, never rounded: a value with more places than the column keeps is not one the column could have held.
    private static long? Hundredths(decimal? value) => value switch
    {
        null => null,
        { } v when decimal.Truncate(v * 100m) == v * 100m => decimal.ToInt64(v * 100m),
        { } v => throw new InvalidOperationException(
            $"A nutrition value has {v.Scale} decimal places; its column keeps {RecipeLimits.NutritionScale}."),
    };
}
