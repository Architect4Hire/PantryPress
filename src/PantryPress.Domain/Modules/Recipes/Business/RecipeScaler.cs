using System.Collections.Frozen;
using System.Text;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// An ingredient line as stored (PP4.3) or as a revision froze it (PP4.4): the blogger's text and the parts the parser
/// read from it (PP4.5), which only enrich it. The spans index <see cref="OriginalText"/> in UTF-16 code units.
/// </summary>
public sealed record ScalableIngredientLine(
    string OriginalText,
    IngredientParseStatus Status,
    Quantity? Quantity,
    TextSpan? QuantitySpan,
    string? UnitCode,
    TextSpan? UnitSpan,
    string? Item)
{
    /// <summary>The line as the parser read it, which is how every writer stores it (PP4.11).</summary>
    public static ScalableIngredientLine From(string originalText, IngredientLineParse parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        return new(originalText, parse.Status, parse.Quantity, parse.QuantitySpan, parse.UnitCode, parse.UnitSpan, parse.Item);
    }
}

/// <summary>
/// An ingredient line as a reader sees it at one scale (RCP-004; PP4.7).
/// </summary>
/// <param name="Text">The line: the blogger's text with only the quantity, and the unit's number, written anew.</param>
/// <param name="Quantity">The amount <paramref name="Text"/> shows, after rounding; the line's own when it was not scaled.</param>
/// <param name="QuantitySpan">Where that amount is in <paramref name="Text"/>.</param>
/// <param name="IsRounded">
/// Whether a scaled amount is shown that differs from the exact one. A line shown as written is never "rounded", even when
/// its own amount is what the exact one rounded back to (<c>1/4 tsp</c> at ¾× is 3/16 exactly, and shows <c>1/4 tsp</c>).
/// </param>
/// <param name="Unscaled">
/// Why the line is shown as written though it has a number scaling would change, or - in a converted variant (PP4.8) - a
/// measure conversion would; none otherwise.
/// </param>
/// <param name="IsConverted">
/// Whether a converted variant (PP4.8) wrote any of the line in the other system: its measure, or a temperature in it. Never
/// set by <see cref="RecipeScaler"/>. <paramref name="IsRounded"/> is always about <paramref name="Quantity"/> alone - the
/// line's own amount, scaled, converted, or a temperature line's temperature - and never about a temperature elsewhere in
/// the text, which a conversion always rounds.
/// </param>
public sealed record ScaledIngredientLine(
    string Text,
    Quantity? Quantity,
    TextSpan? QuantitySpan,
    bool IsRounded,
    UnscaledReason? Unscaled,
    bool IsConverted = false);

/// <summary>A recipe's yield at one scale: the amount, its text, and whether it was rounded. The yield's word is the blogger's.</summary>
public sealed record ScaledYield(Quantity Quantity, string Text, bool IsRounded);

/// <summary>
/// Scales a recipe for a reader (RCP-004, PP-D05; PP4.7): pure and deterministic, exact until the one rounding step, with no
/// floating point, culture, or clock. Every variant a recipe card offers is made here when page data is built (RND-003); the
/// browser only switches between them. The blogger's wording is kept (RCP-002): only the quantity span is written anew, and
/// the unit's span only to agree in number with it, through <see cref="UnitInflections"/>.
/// <list type="number">
/// <item>At 1× every line is exactly as written.</item>
/// <item>A line the parser could not read is shown as written, flagged <see cref="UnscaledReason.Unparsed"/>.</item>
/// <item>A temperature is never scaled; its line is shown as written, unflagged.</item>
/// <item>A line with a number outside its quantity - other than a size between the quantity and the unit or item, a
/// percentage, or a temperature, which describe the item rather than measure it - is shown as written, flagged
/// <see cref="UnscaledReason.OtherAmounts"/>: scaling the quantity alone would be wrong.</item>
/// <item>A line with no amount ("salt", "to taste") is shown as written, unflagged: there is nothing to scale.</item>
/// <item>Otherwise the amount is multiplied exactly, each end rounded by <see cref="FriendlyRounding"/> on the grid the
/// unit and the blogger's style choose, and a range whose ends round alike becomes one amount. An amount that rounds back to
/// the line's own is shown as written.</item>
/// <item>The new line is read again, and must give back exactly the new amount; when a size's fraction would join it
/// (<c>2 ½-inch</c>), the line is shown as written, flagged <see cref="UnscaledReason.OtherAmounts"/>.</item>
/// </list>
/// The amount is written the way the blogger wrote theirs: decimals stay decimals, ASCII fractions stay ASCII
/// (<c>1 1/2</c>), and anything else - fraction characters, whole numbers, number words, <c>a</c> - is written with
/// fraction characters (<c>1½</c>). Metric units are always written as decimals, and a range keeps its <c>to</c>; a dash
/// becomes an en dash. Steps are never scaled: they are prose.
/// </summary>
public static class RecipeScaler
{
    private const char FractionSlash = (char)0x2044;

    // Inexact measures: never a part of one.
    private static readonly HashSet<string> WholeUnits = new(["pinch", "dash", "drop", "handful"], StringComparer.Ordinal);

    internal static readonly FrozenDictionary<string, UnitEntry> UnitsByCode =
        Units.All.ToFrozenDictionary(u => u.Code, StringComparer.Ordinal);

    public static ScaledIngredientLine ScaleLine(ScalableIngredientLine line, ScaleFactor factor)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(factor);
        var unit = Validate(line);

        var asWritten = new ScaledIngredientLine(line.OriginalText, line.Quantity, line.QuantitySpan, IsRounded: false, Unscaled: null);
        if (factor.IsUnchanged)
        {
            return asWritten;
        }

        if (line.Status == IngredientParseStatus.Unparsed)
        {
            return asWritten with { Unscaled = UnscaledReason.Unparsed };
        }

        if (unit?.Dimension == UnitDimension.Temperature)
        {
            return asWritten;
        }

        if (HasOtherAmounts(line))
        {
            return asWritten with { Unscaled = UnscaledReason.OtherAmounts };
        }

        if (line.Quantity is not { HasAmount: true } quantity)
        {
            return asWritten;
        }

        var span = line.QuantitySpan!.Value;
        var written = line.OriginalText.Substring(span.Start, span.Length);
        var grid = GridFor(unit, written);
        var exact = quantity.MultiplyBy(factor.Value);
        var rounded = Round(exact, grid);
        if (rounded == quantity)
        {
            return asWritten;
        }

        var quantityText = Write(rounded, grid, written);
        var text = new StringBuilder(line.OriginalText.Length + quantityText.Length)
            .Append(line.OriginalText, 0, span.Start)
            .Append(quantityText);
        var after = span.End;
        if (line.UnitSpan is { } unitSpan)
        {
            var plural = (rounded.High ?? rounded.Low!.Value) > Rational.One;
            text.Append(line.OriginalText, after, unitSpan.Start - after)
                .Append(UnitInflections.Agree(line.OriginalText.Substring(unitSpan.Start, unitSpan.Length), plural));
            after = unitSpan.End;
        }

        text.Append(line.OriginalText, after, line.OriginalText.Length - after);
        var scaled = text.ToString();
        var scaledSpan = new TextSpan(span.Start, quantityText.Length);
        if (!ReadsBackAs(scaled, rounded, scaledSpan))
        {
            return asWritten with { Unscaled = UnscaledReason.OtherAmounts };
        }

        return new ScaledIngredientLine(scaled, rounded, scaledSpan, IsRounded: rounded != exact, Unscaled: null);
    }

    /// <summary>
    /// The yield at <paramref name="factor"/> (<c>Makes 24 cookies</c> halved makes 12), rounded on the kitchen grid and
    /// written with fraction characters; the yield's word is the blogger's and is never changed.
    /// </summary>
    public static ScaledYield ScaleYield(Quantity yield, ScaleFactor factor)
    {
        ArgumentNullException.ThrowIfNull(yield);
        ArgumentNullException.ThrowIfNull(factor);
        if (!yield.HasAmount)
        {
            throw new ArgumentException("A yield is an amount or a range, never \"to taste\".", nameof(yield));
        }

        if (factor.IsUnchanged)
        {
            return new ScaledYield(yield, QuantityText.Format(yield, QuantityStyle.Fraction), IsRounded: false);
        }

        var exact = yield.MultiplyBy(factor.Value);
        var rounded = Round(exact, RoundingGrid.Kitchen);
        return new ScaledYield(rounded, QuantityText.Format(rounded, QuantityStyle.Fraction), IsRounded: rounded != exact);
    }

    // The unit, after checking the line is one the parser could have read: a bug otherwise, never a reader's input.
    internal static UnitEntry? Validate(ScalableIngredientLine line)
    {
        ArgumentNullException.ThrowIfNull(line.OriginalText, nameof(line));
        CheckSpan(line.OriginalText, line.QuantitySpan);
        CheckSpan(line.OriginalText, line.UnitSpan);
        if (line.Status == IngredientParseStatus.Unparsed
            && (line.Quantity is not null || line.QuantitySpan is not null || line.UnitCode is not null || line.UnitSpan is not null || line.Item is not null))
        {
            throw new ArgumentException("A line the parser could not read has no parts.", nameof(line));
        }

        if ((line.Quantity is { HasAmount: true } && line.QuantitySpan is null) || (line.Quantity is null && line.QuantitySpan is not null))
        {
            throw new ArgumentException("A quantity and its span come together.", nameof(line));
        }

        // A stored term is at most the cap, which keeps every scaled term below 10^12 (ScaleFactor).
        if (line.Quantity is { } quantity && !WithinCap(quantity))
        {
            throw new ArgumentException("A quantity's terms are at most the stored cap.", nameof(line));
        }

        if (line.UnitSpan is { } unitSpan && line.QuantitySpan is { } quantitySpan && unitSpan.Start < quantitySpan.End)
        {
            throw new ArgumentException("A unit comes after its quantity.", nameof(line));
        }

        if (line.UnitCode is null)
        {
            return line.UnitSpan is null ? null : throw new ArgumentException("A unit's span comes with its code.", nameof(line));
        }

        if (line.UnitSpan is null || line.Quantity is not { HasAmount: true })
        {
            throw new ArgumentException("A unit comes only with an amount or a range, and always with its span.", nameof(line));
        }

        return UnitsByCode.TryGetValue(line.UnitCode, out var unit)
            ? unit
            : throw new ArgumentException($"'{line.UnitCode}' is not a unit in the vocabulary.", nameof(line));
    }

    private static void CheckSpan(string text, TextSpan? span)
    {
        if (span is { } s && (s.Start < 0 || s.Length <= 0 || s.End > text.Length))
        {
            throw new ArgumentException("A span lies inside the line.", nameof(span));
        }
    }

    private static RoundingGrid GridFor(UnitEntry? unit, string written)
    {
        if (unit is not null && WholeUnits.Contains(unit.Code))
        {
            return RoundingGrid.Whole;
        }

        if (unit?.System == UnitSystem.Metric)
        {
            return RoundingGrid.Metric;
        }

        // A point in a quantity's span is only ever a decimal point.
        return written.Contains('.', StringComparison.Ordinal) ? RoundingGrid.KitchenDecimal : RoundingGrid.Kitchen;
    }

    // A reader must see the new amount as the amount it is. A size that starts with a fraction, straight after a new whole
    // amount, would join it into one mixed number ("a ½-inch piece" doubled is "2 ½-inch", which reads as 2½), so the line
    // is read again - past the stored length cap, since a scaled line may grow - and must give back exactly the new amount
    // at its span. Known gap: the parser reads no term past the stored cap, so an amount past it (1000000 g tripled) is not
    // checked, and a size's fraction joining one would go unseen; such a line is display only and contrived.
    internal static bool ReadsBackAs(string text, Quantity rounded, TextSpan span)
    {
        if (!WithinCap(rounded))
        {
            return true;
        }

        var reread = IngredientLineParser.ParseAnyLength(text);
        return reread.Quantity == rounded && reread.QuantitySpan == span;
    }

    internal static bool WithinCap(Quantity quantity) =>
        new[] { quantity.Low, quantity.High }.OfType<Rational>()
            .All(v => v.Numerator <= RecipeLimits.MaxQuantityTerm && v.Denominator <= RecipeLimits.MaxQuantityTerm);

    internal static Quantity Round(Quantity exact, RoundingGrid grid)
    {
        var low = FriendlyRounding.Round(exact.Low!.Value, grid);
        if (exact.Kind == QuantityKind.Amount)
        {
            return Quantity.Amount(low);
        }

        var high = FriendlyRounding.Round(exact.High!.Value, grid);
        return high == low ? Quantity.Amount(low) : Quantity.Range(low, high);
    }

    internal static string Write(Quantity rounded, RoundingGrid grid, string written)
    {
        var style = grid is RoundingGrid.Metric or RoundingGrid.KitchenDecimal ? QuantityStyle.Decimal : QuantityStyle.Fraction;

        // A decimal writer's rare fraction (a sixteenth) is ASCII too, like the rest of their numbers.
        var ascii = grid == RoundingGrid.KitchenDecimal || (grid == RoundingGrid.Kitchen && WritesAsciiFractions(written));
        var text = QuantityText.Format(rounded, style, ascii);
        return rounded.Kind == QuantityKind.Range && RangeWord(written) is { } to
            ? string.Join(" " + to + " ", text.Split(QuantityText.RangeSeparator))
            : text;
    }

    private static bool WritesAsciiFractions(string written) =>
        (written.Contains('/', StringComparison.Ordinal) || written.Contains(FractionSlash, StringComparison.Ordinal))
        && !IngredientLineTokenizer.Tokenize(written).Any(t => t.Kind == IngredientTokenKind.FractionGlyph);

    // The "to" a range was written with, in the blogger's case; none for a dash.
    internal static string? RangeWord(string written)
    {
        foreach (var token in IngredientLineTokenizer.Tokenize(written))
        {
            if (token.Kind == IngredientTokenKind.Word && IsAsciiWord(written, token, "to"))
            {
                return written.Substring(token.Start, token.Length);
            }
        }

        return null;
    }

    // A number outside the quantity span and outside the size between the quantity and the unit or item. A percentage
    // ("2% milk") describes the item, so it is not an amount.
    internal static bool HasOtherAmounts(ScalableIngredientLine line)
    {
        var text = line.OriginalText;
        var (sizeStart, sizeEnd) = SizeRegion(line);
        var tokens = IngredientLineTokenizer.Tokenize(text);
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!IsNumber(text, token)
                || (line.QuantitySpan is { } q && token.Start >= q.Start && token.End <= q.End)
                || (token.Start >= sizeStart && token.End <= sizeEnd))
            {
                continue;
            }

            if (!Describes(text, tokens, i))
            {
                return true;
            }
        }

        return false;
    }

    // Between the amount and its unit, or the item when there is no unit: where the parser reads sizes (PP4.5).
    private static (int Start, int End) SizeRegion(ScalableIngredientLine line)
    {
        if (line.Quantity is not { HasAmount: true } || line.QuantitySpan is not { } quantitySpan)
        {
            return (0, 0);
        }

        var start = quantitySpan.End;
        if (line.UnitSpan is { } unitSpan)
        {
            return (start, unitSpan.Start);
        }

        var item = line.Item is null ? -1 : line.OriginalText.IndexOf(line.Item, start, StringComparison.Ordinal);
        return item < 0 ? (0, 0) : (start, item);
    }

    private static bool IsNumber(string text, IngredientToken token) => token.Kind switch
    {
        IngredientTokenKind.Digits or IngredientTokenKind.FractionGlyph or IngredientTokenKind.OtherNumber => true,
        IngredientTokenKind.Word => IngredientLineParser.AmountWords.Any(w => IsAsciiWord(text, token, w)),
        _ => false,
    };

    // A number that describes the item rather than measuring it, so scaling cannot make it wrong: a percentage joined to
    // its sign ("2% milk", "3.25%"), a temperature ("110 °F", "65°F"), or the first end of a range of either
    // ("105-115°F"). Tokens next to each other in the list are joined, because whitespace is a token of its own.
    private static bool Describes(string text, IReadOnlyList<IngredientToken> tokens, int i)
    {
        var k = i + 1;
        if (tokens[i].Kind == IngredientTokenKind.Digits && k + 1 < tokens.Count
            && tokens[k].Kind == IngredientTokenKind.Point && tokens[k + 1].Kind == IngredientTokenKind.Digits)
        {
            k += 2;
        }

        if (k >= tokens.Count)
        {
            return false;
        }

        if (tokens[k].Kind == IngredientTokenKind.Symbol && text[tokens[k].Start] == '%')
        {
            return true;
        }

        var next = SkipSpace(tokens, k);
        if (next >= tokens.Count)
        {
            return false;
        }

        if (UnitSpellings.Match(text, tokens[next].Start) is { } unit && UnitsByCode[unit.Code].Dimension == UnitDimension.Temperature)
        {
            return true;
        }

        if (tokens[next].Kind != IngredientTokenKind.Dash)
        {
            return false;
        }

        var other = SkipSpace(tokens, next + 1);
        return other < tokens.Count && IsNumber(text, tokens[other]) && Describes(text, tokens, other);
    }

    private static int SkipSpace(IReadOnlyList<IngredientToken> tokens, int i) =>
        i < tokens.Count && tokens[i].Kind == IngredientTokenKind.Space ? i + 1 : i;

    // The token is exactly this lower-case word in any ASCII case; only ASCII letters fold, as the parser reads words.
    private static bool IsAsciiWord(string text, IngredientToken token, string lower)
    {
        if (token.Length != lower.Length)
        {
            return false;
        }

        for (var c = 0; c < lower.Length; c++)
        {
            var actual = text[token.Start + c];
            if ((char.IsAsciiLetterUpper(actual) ? (char)(actual + ('a' - 'A')) : actual) != lower[c])
            {
                return false;
            }
        }

        return true;
    }
}
