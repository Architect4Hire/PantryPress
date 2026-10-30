using System.Globalization;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// The display text of a quantity (RCP-004; PP4.2), the same for the same value and style on every machine: digits are
/// ASCII, the decimal point is <c>.</c>, and no culture is read. The text is always exact - nothing here rounds - so
/// rounding to friendly fractions before display is the scaling service's (PP4.7).
/// <list type="bullet">
/// <item>Fraction style writes a mixed number. Halves, thirds, quarters, and eighths are one character joined to the whole
/// number (<c>1½</c>, <c>⅞</c>); any other remainder is ASCII after a space (<c>1 5/16</c>, <c>2/5</c>).</item>
/// <item>Decimal style writes a value whose decimal ends within <see cref="DecimalPlaces"/> places, with a leading zero
/// and no trailing ones (<c>0.5</c>, <c>0.125</c>), and writes any other value in fraction style rather than round it. A
/// range is written in decimals only when both ends can be, so its two ends never mix styles.</item>
/// <item>A range is its two ends joined by an en dash with no spaces (<c>2–3</c>). "To taste" is <see cref="ToTaste"/>,
/// the structured line's label in the editor and a paste-to-parse proposal; a reader sees the blogger's own words for such
/// a line, since a quantity span that never scales is never replaced (RCP-002).</item>
/// </list>
/// Text is never parsed back into a quantity here: reading what a blogger wrote is the parser's (PP4.5).
/// </summary>
public static class QuantityText
{
    public const string ToTaste = "to taste";

    public const string RangeSeparator = "–";

    public const int DecimalPlaces = 3;

    // 10 to the DecimalPlaces: a value's decimal ends within that many places exactly when its denominator divides it.
    private const long DecimalScale = 1_000;

    private static readonly Dictionary<(long Numerator, long Denominator), string> Glyphs = new()
    {
        [(1, 2)] = "½",
        [(1, 3)] = "⅓",
        [(2, 3)] = "⅔",
        [(1, 4)] = "¼",
        [(3, 4)] = "¾",
        [(1, 8)] = "⅛",
        [(3, 8)] = "⅜",
        [(5, 8)] = "⅝",
        [(7, 8)] = "⅞",
    };

    public static string Format(Quantity quantity, QuantityStyle style)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        RequireStyle(style);
        return quantity.Kind switch
        {
            QuantityKind.Amount => Format(quantity.Low!.Value, style),
            QuantityKind.Range => FormatRange(quantity.Low!.Value, quantity.High!.Value, style),
            QuantityKind.ToTaste => ToTaste,
            _ => throw new ArgumentOutOfRangeException(nameof(quantity), quantity.Kind, "Not a quantity kind."),
        };
    }

    /// <summary>
    /// One value's text, for any value a <see cref="Rational"/> holds. Zero is <c>0</c> and a value below zero takes a
    /// leading <c>-</c>, for a converted temperature (PP4.8); a quantity itself is always above zero.
    /// </summary>
    public static string Format(Rational value, QuantityStyle style)
    {
        RequireStyle(style);

        // The magnitude in 128 bits, so even long.MinValue's has a positive spelling.
        var numerator = Int128.Abs(value.Numerator);
        var sign = value.Sign < 0 ? "-" : string.Empty;
        return sign + (style == QuantityStyle.Decimal && IsExactDecimal(value)
            ? FormatDecimal(numerator, value.Denominator)
            : FormatFraction(numerator, value.Denominator));
    }

    private static void RequireStyle(QuantityStyle style)
    {
        if (style is not (QuantityStyle.Fraction or QuantityStyle.Decimal))
        {
            throw new ArgumentOutOfRangeException(nameof(style), style, "Not a quantity style.");
        }
    }

    private static string FormatRange(Rational low, Rational high, QuantityStyle style)
    {
        var both = style == QuantityStyle.Decimal && !(IsExactDecimal(low) && IsExactDecimal(high))
            ? QuantityStyle.Fraction
            : style;
        return Format(low, both) + RangeSeparator + Format(high, both);
    }

    private static bool IsExactDecimal(Rational value) => DecimalScale % value.Denominator == 0;

    // numerator is zero or above; the remainder is below the denominator, so it fits in a long.
    private static string FormatFraction(Int128 numerator, long denominator)
    {
        var whole = numerator / denominator;
        var remainder = (long)(numerator % denominator);
        if (remainder == 0)
        {
            return Invariant(whole);
        }

        var wholeText = whole == 0 ? string.Empty : Invariant(whole);
        if (Glyphs.TryGetValue((remainder, denominator), out var glyph))
        {
            return wholeText + glyph;
        }

        var fraction = Invariant(remainder) + "/" + Invariant(denominator);
        return whole == 0 ? fraction : wholeText + " " + fraction;
    }

    // numerator is zero or above, and the denominator divides DecimalScale. Only the remainder is scaled, and it is below
    // the denominator, so the places never overflow whatever the whole part is.
    private static string FormatDecimal(Int128 numerator, long denominator)
    {
        var whole = Invariant(numerator / denominator);
        var places = (long)(numerator % denominator) * (DecimalScale / denominator);
        return places == 0
            ? whole
            : whole + "." + Invariant(places).PadLeft(DecimalPlaces, '0').TrimEnd('0');
    }

    private static string Invariant(Int128 number) => number.ToString(CultureInfo.InvariantCulture);
}
