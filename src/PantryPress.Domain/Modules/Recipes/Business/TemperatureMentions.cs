using System.Globalization;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// One temperature written in a line or a step (PP4.8): a number or a range of two, then a temperature unit.
/// </summary>
/// <param name="Numbers">The number, or both ends of a range and what joins them (<c>350</c>, <c>325-350</c>, <c>325 to 350</c>).</param>
/// <param name="Unit">The unit as written (<c>°F</c>, <c>degrees C</c>), after any whitespace that follows the numbers.</param>
/// <param name="UnitCode"><c>fahrenheit</c> or <c>celsius</c>.</param>
/// <param name="Low">The number, or the range's low end.</param>
/// <param name="High">The range's high end; none for one number.</param>
/// <param name="RangeWord">The <c>to</c> a range was written with, in the blogger's case; none for a dash or one number.</param>
/// <param name="Readable">
/// False for a temperature that is not converted with confidence: a minus sign or a joined dash before it, or a range whose
/// high end is not above its low end. Seeing one leaves every temperature in its text as written.
/// </param>
internal sealed record TemperatureMention(
    TextSpan Numbers,
    TextSpan Unit,
    string UnitCode,
    Rational Low,
    Rational? High,
    string? RangeWord,
    bool Readable);

/// <summary>
/// Finds the temperatures written in a text (RCP-005; PP4.8) in one pass over the parser's tokens, with no regular expression:
/// <code>digits[.digits] [(dash | " to ") digits[.digits]] [whitespace] temperature-unit</code>
/// The unit is a spelling from the vocabulary (<see cref="UnitSpellings"/>), so it carries its scale: <c>350 degrees</c>
/// with no F or C is no temperature, and is never guessed at. A number joined to a word, a digit, a decimal point, a slash,
/// a comma, or a fraction before it is part of something else (<c>110°F/43°C</c>), and a dash joined before it is a minus
/// sign or a range this pass did not start: either is found but not <see cref="TemperatureMention.Readable"/>, so the text
/// keeps every temperature as written.
/// </summary>
internal static class TemperatureMentions
{
    // As the parser: a whole number of at most this many digits, and at most this many after a decimal point.
    private const int MaxDigits = 7;
    private const int MaxDecimalPlaces = 6;

    /// <summary>Every temperature in <paramref name="text"/> that starts at or after <paramref name="from"/>, in order.</summary>
    public static IReadOnlyList<TemperatureMention> Find(string text, int from)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = IngredientLineTokenizer.Tokenize(text);
        var found = new List<TemperatureMention>();
        var previousUnitEnd = -1;
        var i = 0;
        while (i < tokens.Count)
        {
            if (tokens[i].Start < from || tokens[i].Kind != IngredientTokenKind.Digits)
            {
                i++;
                continue;
            }

            if (Read(text, tokens, i, previousUnitEnd) is not { } mention)
            {
                i++;
                continue;
            }

            // A temperature joined to a number before it ("110°F/43°C", "1,350°F") is found, so its text is left as written.
            found.Add(JoinedToNumberBefore(tokens, i) ? mention.Mention with { Readable = false } : mention.Mention);
            previousUnitEnd = mention.Mention.Unit.End;
            i = mention.Next;
        }

        return found;
    }

    private static (TemperatureMention Mention, int Next)? Read(string text, IReadOnlyList<IngredientToken> tokens, int i, int previousUnitEnd)
    {
        if (ReadNumber(text, tokens, i) is not { } low)
        {
            return null;
        }

        var numbersEnd = tokens[low.Next - 1].End;
        var next = low.Next;
        Rational? high = null;
        string? rangeWord = null;

        // A range: a dash with or without spaces around it, or "to" between spaces.
        var afterLow = SkipSpace(tokens, next);
        if (afterLow < tokens.Count && tokens[afterLow].Kind == IngredientTokenKind.Dash
            && ReadNumber(text, tokens, SkipSpace(tokens, afterLow + 1)) is { } dashed)
        {
            (high, next) = (dashed.Value, dashed.Next);
        }
        else if (afterLow > next && afterLow + 2 < tokens.Count && IsTo(text, tokens[afterLow])
            && tokens[afterLow + 1].Kind == IngredientTokenKind.Space
            && ReadNumber(text, tokens, afterLow + 2) is { } worded)
        {
            (high, next, rangeWord) = (worded.Value, worded.Next, text.Substring(tokens[afterLow].Start, tokens[afterLow].Length));
        }

        if (high is not null)
        {
            numbersEnd = tokens[next - 1].End;
        }

        var unitAt = SkipSpace(tokens, next);
        if (unitAt >= tokens.Count
            || UnitSpellings.Match(text, tokens[unitAt].Start) is not { } unit
            || !IsTemperature(unit.Code))
        {
            // "325 to 350 degrees" names no scale; "325-350°F" is read from its first number, never from its second.
            return null;
        }

        var unitSpan = new TextSpan(tokens[unitAt].Start, unit.Length);
        var readable = !MinusBefore(tokens, i, previousUnitEnd) && (high is not { } h || h > low.Value);
        var mention = new TemperatureMention(
            new TextSpan(tokens[i].Start, numbersEnd - tokens[i].Start), unitSpan, unit.Code, low.Value, high, rangeWord, readable);
        var after = next;
        while (after < tokens.Count && tokens[after].Start < unitSpan.End)
        {
            after++;
        }

        return (mention, after);
    }

    // digits, or digits '.' digits, all joined, and not followed by a joined fraction, slash, or another decimal point.
    private static (Rational Value, int Next)? ReadNumber(string text, IReadOnlyList<IngredientToken> tokens, int i)
    {
        if (i >= tokens.Count || tokens[i].Kind != IngredientTokenKind.Digits || tokens[i].Length > MaxDigits)
        {
            return null;
        }

        var value = (Rational)long.Parse(text.AsSpan(tokens[i].Start, tokens[i].Length), CultureInfo.InvariantCulture);
        var next = i + 1;
        if (next + 1 < tokens.Count && tokens[next].Kind == IngredientTokenKind.Point && tokens[next + 1].Kind == IngredientTokenKind.Digits)
        {
            var places = tokens[next + 1];
            if (places.Length > MaxDecimalPlaces)
            {
                return null;
            }

            var scale = 1L;
            for (var p = 0; p < places.Length; p++)
            {
                scale *= 10;
            }

            value += Rational.Create(long.Parse(text.AsSpan(places.Start, places.Length), CultureInfo.InvariantCulture), scale);
            next += 2;
        }

        if (next < tokens.Count && tokens[next].Kind is IngredientTokenKind.Point or IngredientTokenKind.Slash
            or IngredientTokenKind.FractionGlyph or IngredientTokenKind.Digits or IngredientTokenKind.OtherNumber)
        {
            return null;
        }

        return (value, next);
    }

    // A number joined to what comes before it is part of that: "A1", "1,350", "3.5", "1/350", "½350".
    private static bool JoinedToNumberBefore(IReadOnlyList<IngredientToken> tokens, int i) =>
        i > 0 && tokens[i - 1].Kind is IngredientTokenKind.Word or IngredientTokenKind.Digits or IngredientTokenKind.Point
            or IngredientTokenKind.Slash or IngredientTokenKind.FractionGlyph or IngredientTokenKind.OtherNumber or IngredientTokenKind.Comma;

    // A dash joined to the number: a minus sign ("-18°C"), or the second end of a range this pass did not start ("a-350°F").
    // A dash after the last temperature's unit and a space joins two whole temperatures ("160°F -165°F"), each read on its
    // own. With no space ("160°F-165°F") the first is never found - a unit does not end a word before a hyphen
    // (UnitSpellings) - so the second is a minus sign, and the text stays as written.
    private static bool MinusBefore(IReadOnlyList<IngredientToken> tokens, int i, int previousUnitEnd)
    {
        if (i == 0 || tokens[i - 1].Kind != IngredientTokenKind.Dash)
        {
            return false;
        }

        var before = i - 2;
        if (before >= 0 && tokens[before].Kind == IngredientTokenKind.Space)
        {
            before--;
        }

        return before < 0 || tokens[before].End != previousUnitEnd;
    }

    private static int SkipSpace(IReadOnlyList<IngredientToken> tokens, int i) =>
        i < tokens.Count && tokens[i].Kind == IngredientTokenKind.Space ? i + 1 : i;

    private static bool IsTo(string text, IngredientToken token) =>
        token.Kind == IngredientTokenKind.Word && token.Length == 2
        && (text[token.Start] | 0x20) == 't' && (text[token.Start + 1] | 0x20) == 'o';

    private static bool IsTemperature(string code) => RecipeScaler.UnitsByCode[code].Dimension == UnitDimension.Temperature;
}
