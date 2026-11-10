using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// How much of its dimension's base one unit is (RCP-005; PP4.8): milliliters for volume, grams for weight. Exact, from the
/// units' legal definitions, never a rounded figure.
/// </summary>
public sealed record UnitConversionEntry(string UnitCode, Rational InBase, int Since);

/// <summary>
/// The exact factors US and metric volume and weight convert by (RCP-005, PP-D05; PP4.8), as platform reference data
/// (PP-D06): no site, read-only, never taken from a request. US measures come from their definitions: a US gallon is 231
/// cubic inches, exactly 3.785411784 liters, and a teaspoon is 1/768 of one, so a teaspoon is exactly 157725491/32000000
/// ml; a pound is exactly 453.59237 g (the 1959 international pound) and an ounce 1/16 of it. A fluid ounce is two
/// tablespoons and a cup 16. Count units and temperatures have no factor here: counts never convert, and a temperature is an
/// offset as well as a scale (<see cref="RecipeConverter"/>). This list is code only: nothing stored refers to it, and page
/// data is built in .NET, so it has no table and is not <c>IReferenceData</c> (owner decision, PP4.8; backend.md). To change
/// a factor - a correction only, since each is a definition - bump <see cref="Version"/> and set the entry's <c>Since</c> to
/// it. A change reaches published recipes when their page data is next built (PP7.11).
/// </summary>
public static class UnitConversions
{
    /// <summary>The list's version: the highest <c>Since</c> among its entries.</summary>
    public const int Version = 1;

    /// <summary>One US teaspoon in milliliters, exactly.</summary>
    public static Rational TeaspoonMilliliters { get; } = Rational.Create(157_725_491, 32_000_000);

    /// <summary>One US cup (48 teaspoons) in milliliters, exactly.</summary>
    public static Rational CupMilliliters { get; } = TeaspoonMilliliters * 48;

    /// <summary>One pound in grams, exactly.</summary>
    public static Rational PoundGrams { get; } = Rational.Create(45_359_237, 100_000);

    public static IReadOnlyList<UnitConversionEntry> All { get; } =
    [
        new("teaspoon", TeaspoonMilliliters, 1),
        new("tablespoon", TeaspoonMilliliters * 3, 1),
        new("fluid-ounce", TeaspoonMilliliters * 6, 1),
        new("cup", CupMilliliters, 1),
        new("pint", CupMilliliters * 2, 1),
        new("quart", CupMilliliters * 4, 1),
        new("gallon", CupMilliliters * 16, 1),
        new("milliliter", Rational.One, 1),
        new("deciliter", 100, 1),
        new("liter", 1_000, 1),
        new("ounce", PoundGrams / 16, 1),
        new("pound", PoundGrams, 1),
        new("gram", Rational.One, 1),
        new("kilogram", 1_000, 1),
    ];

    private static readonly Dictionary<string, Rational> ByCode = All.ToDictionary(e => e.UnitCode, e => e.InBase, StringComparer.Ordinal);

    /// <summary>The unit's size in its dimension's base (ml or g), or none for a unit that never converts this way.</summary>
    public static Rational? InBase(string unitCode)
    {
        ArgumentNullException.ThrowIfNull(unitCode);
        return ByCode.TryGetValue(unitCode, out var value) ? value : null;
    }

    /// <summary>Whether <paramref name="unit"/> converts by a factor: volume or weight, US or metric.</summary>
    public static bool Converts(UnitEntry unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return unit.Dimension is UnitDimension.Volume or UnitDimension.Weight && unit.System != UnitSystem.Neutral;
    }
}
