using System.Globalization;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>What one token of an ingredient line is (PP4.5).</summary>
public enum IngredientTokenKind
{
    /// <summary>
    /// A run of whitespace of any kind - spaces, tabs, line breaks, no-break and thin spaces - and of the invisible format
    /// characters a paste brings with it (a byte-order mark, a zero-width space or joiner, a direction mark, a soft hyphen),
    /// which separate what they stand between and are never part of a quantity, a unit, or an edge of the item.
    /// </summary>
    Space,

    /// <summary>A run of the ASCII digits 0 to 9.</summary>
    Digits,

    /// <summary>One vulgar-fraction character, such as <c>½</c> or <c>⅛</c>.</summary>
    FractionGlyph,

    /// <summary>A run of other number characters: full-width or other scripts' digits, superscripts, <c>↉</c>.</summary>
    OtherNumber,

    /// <summary>A run of letters and combining marks.</summary>
    Word,

    /// <summary>A slash or the fraction slash (U+2044).</summary>
    Slash,

    /// <summary>A hyphen or dash: <c>-</c>, U+2010, U+2011, U+2012, <c>–</c>, or <c>—</c>.</summary>
    Dash,

    Point,

    Comma,

    Open,

    Close,

    /// <summary>Any other one character, or one surrogate pair: <c>°</c>, <c>%</c>, <c>+</c>, <c>•</c>, an emoji.</summary>
    Symbol,

    /// <summary>
    /// Half of a surrogate pair standing alone, a control character that is not whitespace, or a code point Unicode has not
    /// assigned.
    /// </summary>
    Invalid,
}

/// <summary>One token: its kind and where it is in the line, in UTF-16 code units, as .NET indexes a string.</summary>
public readonly record struct IngredientToken(IngredientTokenKind Kind, int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>
/// Splits an ingredient line into tokens that cover it exactly, end to end, in one pass and with no regular expression
/// (PP4.5). It reads no culture: characters are classified by Unicode category alone, so the same line gives the same
/// tokens on every machine running the same .NET runtime. The categories come from the runtime's Unicode tables, so a
/// runtime upgrade could move one; the golden corpus is the tripwire. A code point Unicode has not yet assigned is
/// <see cref="IngredientTokenKind.Invalid"/>, so a later assignment can only turn a flagged line into a read one, never
/// change how a read line was read. It never changes the line.
/// </summary>
public static class IngredientLineTokenizer
{
    private static readonly Dictionary<char, Rational> Glyphs = new()
    {
        ['½'] = Rational.Create(1, 2),
        ['⅓'] = Rational.Create(1, 3),
        ['⅔'] = Rational.Create(2, 3),
        ['¼'] = Rational.Create(1, 4),
        ['¾'] = Rational.Create(3, 4),
        ['⅕'] = Rational.Create(1, 5),
        ['⅖'] = Rational.Create(2, 5),
        ['⅗'] = Rational.Create(3, 5),
        ['⅘'] = Rational.Create(4, 5),
        ['⅙'] = Rational.Create(1, 6),
        ['⅚'] = Rational.Create(5, 6),
        ['⅐'] = Rational.Create(1, 7),
        ['⅛'] = Rational.Create(1, 8),
        ['⅜'] = Rational.Create(3, 8),
        ['⅝'] = Rational.Create(5, 8),
        ['⅞'] = Rational.Create(7, 8),
        ['⅑'] = Rational.Create(1, 9),
        ['⅒'] = Rational.Create(1, 10),
    };

    /// <summary>The value of a vulgar-fraction character, which a <see cref="IngredientTokenKind.FractionGlyph"/> is.</summary>
    public static Rational GlyphValue(char glyph) =>
        Glyphs.TryGetValue(glyph, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(glyph), glyph, "Not a fraction character.");

    /// <summary>Whether <paramref name="c"/> is one of the dashes a range or a hyphenated word is written with.</summary>
    public static bool IsDash(char c) => c is '-' or '\u2010' or '\u2011' or '\u2012' or '–' or '—';

    public static IReadOnlyList<IngredientToken> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = new List<IngredientToken>();
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            var kind = Classify(text, i, out var width);
            i += width;
            if (kind is IngredientTokenKind.Space or IngredientTokenKind.Digits or IngredientTokenKind.Word or IngredientTokenKind.OtherNumber)
            {
                while (i < text.Length && Classify(text, i, out var next) == kind)
                {
                    i += next;
                }
            }

            tokens.Add(new IngredientToken(kind, start, i - start));
        }

        return tokens;
    }

    /// <summary>Whether the character at <paramref name="index"/> continues a word: a letter, a digit, a number, or a mark.</summary>
    public static bool IsWordCharacter(string text, int index)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (char.IsLowSurrogate(text[index]) || (char.IsHighSurrogate(text[index]) && !IsPair(text, index)))
        {
            return false;
        }

        return char.IsLetterOrDigit(text, index) || char.IsNumber(text, index) || IsMark(CharUnicodeInfo.GetUnicodeCategory(text, index));
    }

    private static IngredientTokenKind Classify(string text, int index, out int width)
    {
        var c = text[index];
        width = 1;
        if (char.IsHighSurrogate(c))
        {
            if (!IsPair(text, index))
            {
                return IngredientTokenKind.Invalid;
            }

            width = 2;
            return ClassifyByCategory(CharUnicodeInfo.GetUnicodeCategory(text, index));
        }

        if (char.IsLowSurrogate(c))
        {
            return IngredientTokenKind.Invalid;
        }

        if (char.IsWhiteSpace(c))
        {
            return IngredientTokenKind.Space;
        }

        if (char.IsControl(c))
        {
            return IngredientTokenKind.Invalid;
        }

        if (char.IsAsciiDigit(c))
        {
            return IngredientTokenKind.Digits;
        }

        if (Glyphs.ContainsKey(c))
        {
            return IngredientTokenKind.FractionGlyph;
        }

        if (IsDash(c))
        {
            return IngredientTokenKind.Dash;
        }

        return c switch
        {
            '/' or '\u2044' => IngredientTokenKind.Slash,
            '.' => IngredientTokenKind.Point,
            ',' => IngredientTokenKind.Comma,
            '(' => IngredientTokenKind.Open,
            ')' => IngredientTokenKind.Close,
            _ => ClassifyByCategory(CharUnicodeInfo.GetUnicodeCategory(c)),
        };
    }

    /// <summary>Whether <paramref name="c"/> is an invisible format character, which reads as whitespace.</summary>
    public static bool IsFormat(char c) => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format;

    private static IngredientTokenKind ClassifyByCategory(UnicodeCategory category) => category switch
    {
        UnicodeCategory.Format => IngredientTokenKind.Space,
        UnicodeCategory.OtherNotAssigned => IngredientTokenKind.Invalid,
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter => IngredientTokenKind.Word,
        _ when IsMark(category) => IngredientTokenKind.Word,
        UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber => IngredientTokenKind.OtherNumber,
        _ => IngredientTokenKind.Symbol,
    };

    private static bool IsMark(UnicodeCategory category) =>
        category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private static bool IsPair(string text, int index) =>
        index + 1 < text.Length && char.IsHighSurrogate(text[index]) && char.IsLowSurrogate(text[index + 1]);
}
