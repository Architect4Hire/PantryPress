using System.Text;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// One ingredient a cup of which has a known weight: its stable code, grams per US cup (exact), and the item spellings that
/// name it, each folded (<see cref="IngredientDensities.Fold"/>).
/// </summary>
public sealed record IngredientDensityEntry(string Code, Rational GramsPerCup, IReadOnlyList<string> Spellings, int Since);

/// <summary>
/// The small density table (RCP-005; PP4.8): the ingredients whose cups convert to grams and back, as platform reference data
/// (PP-D06) - no site, read-only, never taken from a request, and code only, like <see cref="UnitConversions"/> (owner
/// decision, PP4.8). Volume and weight convert into each other only for an ingredient listed here; everything else converts
/// within its own dimension.
/// <list type="bullet">
/// <item>Only ingredients a metric cook weighs are listed: flours, sugars, butter, cocoa, oats, honey, chocolate chips.
/// Liquids such as milk and oil are left out on purpose, because a metric cook measures them in milliliters.</item>
/// <item>The weights are King Arthur Baking's ingredient weight chart (read 2026-11-10), each per US cup (cocoa and honey
/// are listed per half cup and per tablespoon, and multiplied out). Butter is the exact half pound a cup of it is (two
/// sticks), which the chart rounds to 113 g a half cup. Brown sugar is packed, as the chart and US recipes measure it.</item>
/// <item>An item matches only when it is exactly one of an entry's spellings once folded: ASCII letters lower-cased,
/// every run of whitespace one space, and a curly apostrophe a straight one. The note is never read, so
/// <c>1 cup flour, sifted</c> (measured, then sifted) is all-purpose flour, and <c>1 cup sifted flour</c> matches nothing.
/// <c>flour</c> is all-purpose flour and <c>brown sugar</c> is packed, as a US recipe means them.</item>
/// </list>
/// To change it, bump <see cref="Version"/>; a new or corrected entry takes that version as its <c>Since</c>. Never change a
/// code. A change reaches published recipes when their page data is next built (PP7.11).
/// </summary>
public static class IngredientDensities
{
    /// <summary>The list's version: the highest <c>Since</c> among its entries.</summary>
    public const int Version = 1;

    private const char CurlyApostrophe = (char)0x2019;

    public static IReadOnlyList<IngredientDensityEntry> All { get; } =
    [
        new("all-purpose-flour", 120,
            ["flour", "all-purpose flour", "all purpose flour", "unbleached all-purpose flour", "unbleached all purpose flour",
                "plain flour"],
            1),
        new("bread-flour", 120, ["bread flour", "unbleached bread flour"], 1),
        new("cake-flour", 120, ["cake flour", "unbleached cake flour"], 1),
        new("whole-wheat-flour", 113, ["whole wheat flour", "whole-wheat flour", "wholemeal flour"], 1),
        new("granulated-sugar", 198, ["sugar", "granulated sugar", "white sugar", "granulated white sugar", "white granulated sugar"], 1),
        new("brown-sugar", 213,
            ["brown sugar", "light brown sugar", "dark brown sugar", "packed brown sugar", "packed light brown sugar",
                "packed dark brown sugar", "firmly packed brown sugar", "firmly packed light brown sugar",
                "firmly packed dark brown sugar"],
            1),
        new("confectioners-sugar", 113,
            ["confectioners' sugar", "confectioners sugar", "confectioner's sugar", "powdered sugar", "icing sugar"], 1),
        new("butter", UnitConversions.PoundGrams / 2, ["butter", "unsalted butter", "salted butter"], 1),
        new("cocoa", 84,
            ["cocoa", "cocoa powder", "unsweetened cocoa", "unsweetened cocoa powder", "natural cocoa powder",
                "dutch-process cocoa powder", "dutch process cocoa powder", "dutch-processed cocoa powder"],
            1),
        new("rolled-oats", 89,
            ["oats", "rolled oats", "old-fashioned oats", "old fashioned oats", "old-fashioned rolled oats", "quick oats",
                "quick-cooking oats", "quick cooking oats"],
            1),
        new("honey", 336, ["honey"], 1),
        new("chocolate-chips", 170,
            ["chocolate chips", "semisweet chocolate chips", "semi-sweet chocolate chips", "bittersweet chocolate chips",
                "dark chocolate chips", "milk chocolate chips", "white chocolate chips"],
            1),
    ];

    private static readonly Dictionary<string, IngredientDensityEntry> BySpelling =
        All.SelectMany(e => e.Spellings.Select(s => (s, e))).ToDictionary(p => p.s, p => p.e, StringComparer.Ordinal);

    /// <summary>The entry <paramref name="item"/> names once folded, or none: a density is used only when it is known.</summary>
    public static IngredientDensityEntry? Find(string? item) =>
        item is not null && BySpelling.TryGetValue(Fold(item), out var entry) ? entry : null;

    /// <summary>
    /// The item as it is matched: ASCII letters lower-cased and nothing else folded, every run of whitespace (and of the
    /// invisible format characters the parser reads as whitespace) one space, none at either end, and a curly apostrophe
    /// (U+2019) a straight one.
    /// </summary>
    public static string Fold(string item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var folded = new StringBuilder(item.Length);
        var space = false;
        foreach (var c in item)
        {
            if (char.IsWhiteSpace(c) || IngredientLineTokenizer.IsFormat(c))
            {
                space = folded.Length > 0;
                continue;
            }

            if (space)
            {
                folded.Append(' ');
                space = false;
            }

            folded.Append(c switch
            {
                _ when char.IsAsciiLetterUpper(c) => (char)(c + ('a' - 'A')),
                CurlyApostrophe => '\'',
                _ => c,
            });
        }

        return folded.ToString();
    }
}
