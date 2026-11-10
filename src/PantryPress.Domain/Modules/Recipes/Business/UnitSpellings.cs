namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>A unit found in a line: its vocabulary code and how many UTF-16 code units of the line it covers.</summary>
public readonly record struct UnitMatch(string Code, int Length);

/// <summary>
/// Finds a unit where an ingredient line names one (PP4.5), from <see cref="Units.Aliases"/>, the one list of spellings.
/// What it forgives beyond those exact spellings is this file's rule, and only this:
/// <list type="bullet">
/// <item>a case-insensitive spelling matches in any ASCII case, and only ASCII letters fold, so a look-alike such as the
/// Kelvin sign never becomes a <c>k</c>; a case-sensitive one (<c>T</c>, <c>t</c>) matches exactly;</item>
/// <item>a space in a spelling matches any run of whitespace (<c>fl  oz</c>, a no-break space);</item>
/// <item>the longest spelling wins, a case-sensitive one before a case-insensitive one of the same length, then by
/// spelling, so the answer never depends on the list's order;</item>
/// <item>a unit ends where a word does: not before a letter, digit, number, mark, hyphen, or apostrophe, so <c>1 lemon</c>
/// is no liter and <c>1 T-bone</c> no tablespoon.</item>
/// </list>
/// </summary>
public static class UnitSpellings
{
    private static readonly UnitAliasEntry[] Ordered =
    [
        .. Units.Aliases
            .OrderByDescending(a => a.Alias.Length)
            .ThenByDescending(a => a.CaseSensitive)
            .ThenBy(a => a.Alias, StringComparer.Ordinal),
    ];

    /// <summary>The unit whose spelling starts at <paramref name="start"/> and ends where a word does, or none.</summary>
    public static UnitMatch? Match(string text, int start)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var alias in Ordered)
        {
            var end = MatchAt(text, start, alias.Alias, alias.CaseSensitive);
            if (end > start && EndsWord(text, end))
            {
                return new UnitMatch(alias.UnitCode, end - start);
            }
        }

        return null;
    }

    // The index just past the spelling, or -1. A space in the spelling takes the whole run of whitespace in the line.
    private static int MatchAt(string text, int start, string spelling, bool caseSensitive)
    {
        var i = start;
        foreach (var expected in spelling)
        {
            if (expected == ' ')
            {
                if (i >= text.Length || !char.IsWhiteSpace(text[i]))
                {
                    return -1;
                }

                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }

                continue;
            }

            if (i >= text.Length)
            {
                return -1;
            }

            var actual = caseSensitive ? text[i] : FoldAscii(text[i]);
            if (actual != expected)
            {
                return -1;
            }

            i++;
        }

        return i;
    }

    private static char FoldAscii(char c) => char.IsAsciiLetterUpper(c) ? (char)(c + ('a' - 'A')) : c;

    private static bool EndsWord(string text, int end)
    {
        if (end >= text.Length)
        {
            return true;
        }

        var next = text[end];
        return !(IngredientLineTokenizer.IsWordCharacter(text, end) || IngredientLineTokenizer.IsDash(next) || next is '\'' or '’');
    }
}
