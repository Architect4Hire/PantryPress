using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>One unit word in its singular spelling, and the letters that make it plural.</summary>
public sealed record UnitInflection(string Singular, string PluralSuffix)
{
    public string Plural => Singular + PluralSuffix;
}

/// <summary>
/// The unit words whose number a scaled line may change (RCP-004; PP4.7), so <c>a pinch</c> doubled reads
/// <c>2 pinches</c> and <c>2 cups</c> halved reads <c>1 cup</c>. Only spelled-out words are listed, each a case-insensitive
/// spelling in <see cref="Units.Aliases"/> whose plural is the same spelling of the same unit: an abbreviation
/// (<c>tbsp</c>, <c>lbs</c>, <c>g</c>) reads the same at any amount and is never changed, nor is a temperature, which is
/// never scaled. The item's own words are never changed either: there is no English noun grammar here. Every plural adds
/// letters at the end, so the blogger's spacing inside a two-word unit (<c>fluid  ounce</c>) is kept. Each unit's own words
/// come from <see cref="Units"/>, the one list; only the other spelled-out spellings are listed here. Not stored, so a change
/// needs no migration; a test keeps every pair on the vocabulary.
/// </summary>
public static class UnitInflections
{
    // Spelled-out spellings beside a unit's own singular and plural words.
    private static readonly UnitInflection[] OtherSpellings =
    [
        new("teaspoonful", "s"),
        new("tablespoonful", "s"),
        new("millilitre", "s"),
        new("decilitre", "s"),
        new("litre", "s"),
        new("gramme", "s"),
        new("kilogramme", "s"),
        new("kilo", "s"),
    ];

    public static IReadOnlyList<UnitInflection> All { get; } =
    [
        .. Units.All
            .Where(u => u.Dimension != UnitDimension.Temperature)
            .Select(u => u.Plural.StartsWith(u.Singular, StringComparison.Ordinal)
                ? new UnitInflection(u.Singular, u.Plural[u.Singular.Length..])
                : throw new InvalidOperationException($"The plural of '{u.Singular}' does not add letters to it.")),
        .. OtherSpellings,
    ];

    /// <summary>
    /// The unit as written in <paramref name="unitText"/>, put in the singular or the plural. The text is returned as it is
    /// when it is not a listed word or is already in that number. The case is the blogger's: a suffix added to a word that
    /// ends in a capital is in capitals (<c>CUP</c> to <c>CUPS</c>).
    /// </summary>
    public static string Agree(string unitText, bool plural)
    {
        ArgumentNullException.ThrowIfNull(unitText);
        var folded = Fold(unitText);
        foreach (var word in All)
        {
            if (folded == word.Singular)
            {
                return plural ? unitText + CaseOf(unitText, word.PluralSuffix) : unitText;
            }

            if (folded == word.Plural)
            {
                return plural ? unitText : unitText[..^word.PluralSuffix.Length];
            }
        }

        return unitText;
    }

    /// <summary>
    /// Whether <paramref name="unitText"/> is a spelled-out unit word in either number (<c>cups</c>, <c>Tablespoon</c>,
    /// <c>litre</c>) rather than an abbreviation (<c>tbsp</c>, <c>g</c>), so a converted unit is written the same way (PP4.8).
    /// </summary>
    internal static bool IsWord(string unitText)
    {
        ArgumentNullException.ThrowIfNull(unitText);
        var folded = Fold(unitText);
        return All.Any(w => folded == w.Singular || folded == w.Plural);
    }

    // ASCII letters folded to lower case and every run of whitespace read as one space, the way UnitSpellings matches.
    private static string Fold(string text)
    {
        var folded = new System.Text.StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }

            if (space)
            {
                folded.Append(' ');
                space = false;
            }

            folded.Append(char.IsAsciiLetterUpper(c) ? (char)(c + ('a' - 'A')) : c);
        }

        return folded.ToString();
    }

    private static string CaseOf(string unitText, string suffix) =>
        char.IsAsciiLetterUpper(unitText[^1]) ? suffix.ToUpperInvariant() : suffix;
}
