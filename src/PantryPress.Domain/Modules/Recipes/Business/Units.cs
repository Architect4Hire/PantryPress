using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// One unit: its stable code, its system and dimension, its display words, and the list version that added it. The
/// abbreviation is display text too, and may be absent (a cup is written out).
/// </summary>
public sealed record UnitEntry(
    string Code,
    UnitSystem System,
    UnitDimension Dimension,
    string Singular,
    string Plural,
    string? Abbreviation,
    int Since);

/// <summary>
/// One spelling of a unit a blogger may write. A case-insensitive alias is stored lower-case and matches in any case; a
/// case-sensitive one matches exactly as stored, which is how <c>T</c> is a tablespoon and <c>t</c> a teaspoon.
/// </summary>
public sealed record UnitAliasEntry(string Alias, string UnitCode, bool CaseSensitive, int Since);

/// <summary>
/// The units a recipe measures in (RCP-001, RCP-005; PP4.1), with every spelling the ingredient parser recognizes
/// (PP4.5), as platform reference data (PP-D06, ADR 0006). The aliases are exact spellings: whatever else the parser
/// forgives is the parser's rule, and recognizing a unit never rewrites what the blogger wrote (RCP-002). Volume units are
/// US customary or metric; imperial measures are not listed, so a <c>pint</c> is the US pint. Conversion factors and
/// densities are PP4.8's. This list is the one source: the <c>Units</c> and <c>UnitAliases</c> tables are seeded from
/// it, so a change here changes the model and needs a reviewed migration. To change it, bump <see cref="Version"/>; a new
/// unit takes that version as its <c>Since</c>, and its spellings with it, and a spelling added to a unit already listed
/// goes in <see cref="AddedAliases"/> with that version. Never remove a unit or change a code, because ingredient lines
/// store the code (PP4.3), and never remove a spelling, because a line parsed against it keeps its unit.
/// </summary>
public static class Units
{
    /// <summary>The list's version: the highest <c>Since</c> among its units and aliases.</summary>
    public const int Version = 1;

    public const int WordMaxLength = 40;
    public const int AbbreviationMaxLength = 16;
    public const int AliasMaxLength = 40;

    private static readonly Definition[] Definitions =
    [
        // US customary volume. T and t are the one case-sensitive pair: a capital T is a tablespoon by long habit.
        new(new("teaspoon", UnitSystem.UsCustomary, UnitDimension.Volume, "teaspoon", "teaspoons", "tsp", 1),
            ["teaspoon", "teaspoons", "teaspoonful", "teaspoonfuls", "tsp", "tsp.", "tsps", "tsps."], Exact: ["t"]),
        new(new("tablespoon", UnitSystem.UsCustomary, UnitDimension.Volume, "tablespoon", "tablespoons", "tbsp", 1),
            ["tablespoon", "tablespoons", "tablespoonful", "tablespoonfuls", "tbsp", "tbsp.", "tbsps", "tbsps.", "tbs",
                "tbs.", "tbl", "tbl."],
            Exact: ["T"]),
        new(new("fluid-ounce", UnitSystem.UsCustomary, UnitDimension.Volume, "fluid ounce", "fluid ounces", "fl oz", 1),
            ["fluid ounce", "fluid ounces", "fl oz", "fl. oz.", "fl oz.", "fl.oz.", "fl. oz"]),
        new(new("cup", UnitSystem.UsCustomary, UnitDimension.Volume, "cup", "cups", Abbreviation: null, 1),
            ["cup", "cups", "c", "c."]),
        new(new("pint", UnitSystem.UsCustomary, UnitDimension.Volume, "pint", "pints", "pt", 1),
            ["pint", "pints", "pt", "pt.", "pts", "pts."]),
        new(new("quart", UnitSystem.UsCustomary, UnitDimension.Volume, "quart", "quarts", "qt", 1),
            ["quart", "quarts", "qt", "qt.", "qts", "qts."]),
        new(new("gallon", UnitSystem.UsCustomary, UnitDimension.Volume, "gallon", "gallons", "gal", 1),
            ["gallon", "gallons", "gal", "gal.", "gals", "gals."]),

        // Metric volume, in both spellings.
        new(new("milliliter", UnitSystem.Metric, UnitDimension.Volume, "milliliter", "milliliters", "ml", 1),
            ["milliliter", "milliliters", "millilitre", "millilitres", "ml", "ml.", "mls"]),
        new(new("deciliter", UnitSystem.Metric, UnitDimension.Volume, "deciliter", "deciliters", "dl", 1),
            ["deciliter", "deciliters", "decilitre", "decilitres", "dl", "dl."]),
        new(new("liter", UnitSystem.Metric, UnitDimension.Volume, "liter", "liters", "L", 1),
            ["liter", "liters", "litre", "litres", "l", "l."]),

        // Weight.
        new(new("ounce", UnitSystem.UsCustomary, UnitDimension.Weight, "ounce", "ounces", "oz", 1),
            ["ounce", "ounces", "oz", "oz.", "ozs", "ozs."]),
        new(new("pound", UnitSystem.UsCustomary, UnitDimension.Weight, "pound", "pounds", "lb", 1),
            ["pound", "pounds", "lb", "lb.", "lbs", "lbs."]),
        new(new("gram", UnitSystem.Metric, UnitDimension.Weight, "gram", "grams", "g", 1),
            ["gram", "grams", "gramme", "grammes", "g", "g."]),
        new(new("kilogram", UnitSystem.Metric, UnitDimension.Weight, "kilogram", "kilograms", "kg", 1),
            ["kilogram", "kilograms", "kilogramme", "kilogrammes", "kilo", "kilos", "kg", "kg.", "kgs", "kgs."]),

        // Temperature. No bare F or C: "1 C flour" is a cup. The masculine ordinal (º) is the degree sign's look-alike on
        // many keyboards, so both are listed.
        new(new("fahrenheit", UnitSystem.UsCustomary, UnitDimension.Temperature, "degree Fahrenheit", "degrees Fahrenheit", "°F", 1),
            ["°f", "ºf", "° f", "degree fahrenheit", "degrees fahrenheit", "degree f", "degrees f", "deg f", "fahrenheit"]),
        new(new("celsius", UnitSystem.Metric, UnitDimension.Temperature, "degree Celsius", "degrees Celsius", "°C", 1),
            ["°c", "ºc", "° c", "degree celsius", "degrees celsius", "degree c", "degrees c", "deg c", "celsius", "centigrade"]),

        // Counts and inexact measures: scaled, never converted.
        new(new("pinch", UnitSystem.Neutral, UnitDimension.Count, "pinch", "pinches", Abbreviation: null, 1), ["pinch", "pinches"]),
        new(new("dash", UnitSystem.Neutral, UnitDimension.Count, "dash", "dashes", Abbreviation: null, 1), ["dash", "dashes"]),
        new(new("drop", UnitSystem.Neutral, UnitDimension.Count, "drop", "drops", Abbreviation: null, 1), ["drop", "drops"]),
        new(new("clove", UnitSystem.Neutral, UnitDimension.Count, "clove", "cloves", Abbreviation: null, 1), ["clove", "cloves"]),
        new(new("can", UnitSystem.Neutral, UnitDimension.Count, "can", "cans", Abbreviation: null, 1), ["can", "cans"]),
        new(new("jar", UnitSystem.Neutral, UnitDimension.Count, "jar", "jars", Abbreviation: null, 1), ["jar", "jars"]),
        new(new("package", UnitSystem.Neutral, UnitDimension.Count, "package", "packages", "pkg", 1),
            ["package", "packages", "pkg", "pkg.", "pkgs", "pkgs."]),
        new(new("stick", UnitSystem.Neutral, UnitDimension.Count, "stick", "sticks", Abbreviation: null, 1), ["stick", "sticks"]),
        new(new("slice", UnitSystem.Neutral, UnitDimension.Count, "slice", "slices", Abbreviation: null, 1), ["slice", "slices"]),
        new(new("piece", UnitSystem.Neutral, UnitDimension.Count, "piece", "pieces", "pc", 1), ["piece", "pieces", "pc", "pc.", "pcs", "pcs."]),
        new(new("bunch", UnitSystem.Neutral, UnitDimension.Count, "bunch", "bunches", Abbreviation: null, 1), ["bunch", "bunches"]),
        new(new("sprig", UnitSystem.Neutral, UnitDimension.Count, "sprig", "sprigs", Abbreviation: null, 1), ["sprig", "sprigs"]),
        new(new("head", UnitSystem.Neutral, UnitDimension.Count, "head", "heads", Abbreviation: null, 1), ["head", "heads"]),
        new(new("handful", UnitSystem.Neutral, UnitDimension.Count, "handful", "handfuls", Abbreviation: null, 1), ["handful", "handfuls"]),
    ];

    /// <summary>
    /// Spellings added to a unit after the version that added the unit, each with the version that added it. Empty at
    /// version 1.
    /// </summary>
    private static readonly UnitAliasEntry[] AddedAliases = [];

    public static IReadOnlyList<UnitEntry> All { get; } = [.. Definitions.Select(d => d.Unit)];

    public static IReadOnlyList<UnitAliasEntry> Aliases { get; } =
    [
        .. Definitions.SelectMany(d =>
            d.Aliases.Select(alias => new UnitAliasEntry(alias, d.Unit.Code, CaseSensitive: false, d.Unit.Since))
                .Concat((d.Exact ?? []).Select(alias => new UnitAliasEntry(alias, d.Unit.Code, CaseSensitive: true, d.Unit.Since)))),
        .. AddedAliases,
    ];

    private sealed record Definition(UnitEntry Unit, string[] Aliases, string[]? Exact = null);
}
