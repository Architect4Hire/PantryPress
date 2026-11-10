using System.Text;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>A step as a reader sees it in one measuring system (PP4.8): its text, and whether any temperature in it was converted.</summary>
public sealed record ConvertedStep(string Text, bool IsConverted);

/// <summary>
/// Converts a recipe between US and metric measures for a reader (RCP-005, PP-D05; PP4.8): pure and deterministic, exact until
/// the one rounding step, with no floating point, culture, or clock. A recipe card's toggle has three settings - as written,
/// US, and metric - and each converted variant is made here when page data is built (RND-003), at each scale, so the amount is
/// scaled and converted exactly and rounded once. The blogger's wording is kept (RCP-002): only a measure's quantity and unit
/// spans and a temperature's numbers and unit are written anew.
/// <para>An ingredient line (<see cref="ConvertLine"/>):</para>
/// <list type="number">
/// <item>A line the parser could not read is shown as written, flagged <see cref="UnscaledReason.Unparsed"/>.</item>
/// <item>A temperature line (<c>350°F oven</c>) in the other system is converted, at any scale: temperatures never scale.</item>
/// <item>A line whose measure does not convert - no unit, a count, a unit already in the target system, or a teaspoon or
/// tablespoon in metric, which metric kitchens use too - is what <see cref="RecipeScaler.ScaleLine"/> shows; temperatures in
/// the rest of it (<c>water, heated to 110°F</c>) are still converted.</item>
/// <item>A line whose measure would convert but that has another number outside its quantity is shown as written, flagged
/// <see cref="UnscaledReason.OtherAmounts"/>, as the scaler would: <c>1 cup (240 ml) milk</c> already shows both.</item>
/// <item>Otherwise the exact scaled amount is converted by <see cref="UnitConversions"/>; volume becomes weight in metric, and
/// weight volume in US, only for an item in <see cref="IngredientDensities"/>. The target unit is chosen by the exact value of
/// the amount or the range's low end - ml under 1000, then L; g under 1000, then kg; tsp under a tablespoon, tbsp under a
/// quarter cup, then cups; oz under a pound, then lb - and each end is rounded once by <see cref="FriendlyRounding"/>, on the
/// metric grid for metric and the kitchen grid for US. A size between the quantity and the unit is shown as written, flagged
/// <see cref="UnscaledReason.OtherAmounts"/>.</item>
/// <item>The new line is read again, and must give back exactly the new amount and unit at its span; otherwise it is shown
/// as written, flagged <see cref="UnscaledReason.OtherAmounts"/>.</item>
/// </list>
/// The unit is written the way the blogger wrote theirs: a spelled-out word as the target's word, agreeing with the amount,
/// and an abbreviation as the target's abbreviation (a cup has none, so it is the word). Metric amounts are decimals; US
/// amounts are fractions, ASCII when the blogger's were. A temperature is converted exactly and rounded to a whole degree
/// below 100 °C or 215 °F, and to the nearest 5 degrees at or above (<see cref="RoundTemperature"/>); its unit keeps its
/// spelling with the scale swapped (<c>°F</c> to <c>°C</c>, <c>degrees F</c> to <c>degrees C</c>).
/// <para>
/// Temperatures in a text are converted together or not at all: a text that already names one in the target system
/// (<c>350°F (180°C)</c>), holds one not read with confidence (a minus sign), or holds a range that would start below zero
/// keeps every temperature as written, so a reader never sees two systems mixed by the converter. A single temperature below
/// zero is written with a minus sign (<c>0°F</c> is <c>-18°C</c>). Steps (<see cref="ConvertStep"/>) are prose: only their
/// temperatures convert, and they are never scaled.
/// </para>
/// </summary>
public static class RecipeConverter
{
    private const string Celsius = "celsius";
    private const string Fahrenheit = "fahrenheit";

    // The thresholds where a temperature's rounding moves from whole degrees to fives. Each is a multiple of five, so rounding
    // never puts a warmer temperature below a cooler one: 215 °F rather than 212, which 100 °C still shows as.
    private const int CelsiusFives = 100;
    private const int FahrenheitFives = 215;

    private static readonly Rational TablespoonMilliliters = UnitConversions.TeaspoonMilliliters * 3;
    private static readonly Rational QuarterCupMilliliters = UnitConversions.CupMilliliters / 4;
    private static readonly Rational Thousand = 1_000;

    // Spoons a metric kitchen measures with too: kept as written in a metric variant (owner decision, PP4.8).
    private static readonly HashSet<string> MetricKeeps = new(["teaspoon", "tablespoon"], StringComparer.Ordinal);

    /// <summary>
    /// The line at <paramref name="factor"/> in <paramref name="target"/>'s measures, <see cref="UnitSystem.UsCustomary"/> or
    /// <see cref="UnitSystem.Metric"/>.
    /// </summary>
    public static ScaledIngredientLine ConvertLine(ScalableIngredientLine line, ScaleFactor factor, UnitSystem target)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(factor);
        RequireTarget(target);
        var unit = RecipeScaler.Validate(line);

        var asWritten = new ScaledIngredientLine(line.OriginalText, line.Quantity, line.QuantitySpan, IsRounded: false, Unscaled: null);
        if (line.Status == IngredientParseStatus.Unparsed)
        {
            return asWritten with { Unscaled = UnscaledReason.Unparsed };
        }

        if (unit?.Dimension == UnitDimension.Temperature)
        {
            return ConvertTemperatureLine(line, unit, target, asWritten);
        }

        if (unit is null || !UnitConversions.Converts(unit) || unit.System == target
            || (target == UnitSystem.Metric && MetricKeeps.Contains(unit.Code)))
        {
            return WithTemperatures(line, RecipeScaler.ScaleLine(line, factor), target);
        }

        if (RecipeScaler.HasOtherAmounts(line))
        {
            return asWritten with { Unscaled = UnscaledReason.OtherAmounts };
        }

        return ConvertMeasure(line, unit, factor, target, asWritten);
    }

    /// <summary>
    /// The step in <paramref name="target"/>'s measures: each temperature in the other system converted where it stands, or the
    /// step exactly as written when it has none, already names one in <paramref name="target"/>'s, or holds one not read with
    /// confidence.
    /// </summary>
    public static ConvertedStep ConvertStep(string text, UnitSystem target)
    {
        ArgumentNullException.ThrowIfNull(text);
        RequireTarget(target);
        var mentions = TemperatureMentions.Find(text, 0);
        return ConvertsAll(mentions, target)
            ? new ConvertedStep(ReplaceTemperatures(text, mentions), IsConverted: true)
            : new ConvertedStep(text, IsConverted: false);
    }

    /// <summary>
    /// A converted temperature, rounded: to the nearest whole degree below 100 °C or 215 °F, and to the nearest 5 degrees at
    /// or above, a tie going to the warmer. Both thresholds are multiples of five, so rounding is monotone.
    /// </summary>
    public static Rational RoundTemperature(Rational exact, UnitSystem scale)
    {
        RequireTarget(scale);
        var step = exact >= (scale == UnitSystem.Metric ? CelsiusFives : FahrenheitFives) ? 5 : 1;
        var halfUp = (ExactValue)(exact / step) + Rational.Create(1, 2);
        return checked((long)halfUp.Floor()) * (Rational)step;
    }

    /// <summary>A temperature in degrees of <paramref name="fromCode"/>'s scale, exactly, in the other scale.</summary>
    internal static Rational ConvertTemperature(Rational value, string fromCode) => fromCode switch
    {
        Fahrenheit => (value - 32) * Rational.Create(5, 9),
        Celsius => (value * Rational.Create(9, 5)) + 32,
        _ => throw new ArgumentOutOfRangeException(nameof(fromCode), fromCode, "Not a temperature unit."),
    };

    /// <summary>
    /// A temperature unit as written, with its scale swapped and its spelling kept: the last letter (<c>°F</c>,
    /// <c>degrees f</c>, <c>deg F</c>) or the scale's name (<c>Fahrenheit</c>, <c>celsius</c>, <c>centigrade</c>), in the
    /// blogger's case.
    /// </summary>
    internal static string Counterpart(string unitText, string fromCode)
    {
        ArgumentNullException.ThrowIfNull(unitText);
        var toName = fromCode == Fahrenheit ? "Celsius" : "Fahrenheit";
        string[] names = fromCode == Fahrenheit ? [Fahrenheit] : [Celsius, "centigrade"];
        foreach (var name in names)
        {
            if (unitText.Length >= name.Length && unitText[^name.Length..].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                var start = unitText.Length - name.Length;
                var replacement = char.IsAsciiLetterUpper(unitText[start]) ? toName : toName.ToLowerInvariant();
                return unitText[..start] + replacement;
            }
        }

        var last = unitText[^1];
        var letter = fromCode == Fahrenheit ? 'C' : 'F';
        return unitText[..^1] + (char.IsAsciiLetterUpper(last) ? letter : char.ToLowerInvariant(letter));
    }

    private static void RequireTarget(UnitSystem target)
    {
        if (target is not (UnitSystem.UsCustomary or UnitSystem.Metric))
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "A recipe converts to US or metric measures.");
        }
    }

    private static string TemperatureCode(UnitSystem target) => target == UnitSystem.Metric ? Celsius : Fahrenheit;

    private static ScaledIngredientLine ConvertTemperatureLine(
        ScalableIngredientLine line, UnitEntry unit, UnitSystem target, ScaledIngredientLine asWritten)
    {
        if (unit.System == target)
        {
            return asWritten;
        }

        var quantitySpan = line.QuantitySpan!.Value;
        var mentions = TemperatureMentions.Find(line.OriginalText, 0);
        if (mentions.Count == 0 || mentions[0].Numbers.Start != quantitySpan.Start || mentions[0].Unit != line.UnitSpan
            || !ConvertsAll(mentions, target))
        {
            return asWritten;
        }

        var own = mentions[0];
        var (low, high) = Converted(own);
        if (low.Sign <= 0)
        {
            // A quantity is above zero: a temperature this cold stays as written.
            return asWritten;
        }

        var quantity = high is { } h ? Quantity.Range(low, h) : Quantity.Amount(low);
        var text = ReplaceTemperatures(line.OriginalText, mentions);
        var span = new TextSpan(quantitySpan.Start, NumbersText(own, low, high).Length);
        var reread = IngredientLineParser.ParseAnyLength(text);
        if (reread.Quantity != quantity || reread.QuantitySpan != span || reread.UnitCode != TemperatureCode(target))
        {
            return asWritten with { Unscaled = UnscaledReason.OtherAmounts };
        }

        var exactLow = ConvertTemperature(own.Low, own.UnitCode);
        var exactHigh = own.High is { } ownHigh ? ConvertTemperature(ownHigh, own.UnitCode) : (Rational?)null;
        var isRounded = low != exactLow || (high ?? low) != (exactHigh ?? exactLow);
        return new ScaledIngredientLine(text, quantity, span, isRounded, Unscaled: null, IsConverted: true);
    }

    private static ScaledIngredientLine ConvertMeasure(
        ScalableIngredientLine line, UnitEntry unit, ScaleFactor factor, UnitSystem target, ScaledIngredientLine asWritten)
    {
        var text = line.OriginalText;
        var quantitySpan = line.QuantitySpan!.Value;
        var unitSpan = line.UnitSpan!.Value;
        var gap = text[quantitySpan.End..unitSpan.Start];
        if (!gap.All(c => char.IsWhiteSpace(c) || IngredientLineTokenizer.IsFormat(c)))
        {
            // A size between the amount and the unit ("1 (14 oz) cup") would be left in the other system.
            return asWritten with { Unscaled = UnscaledReason.OtherAmounts };
        }

        // Exact all the way: the scaled amount, in the dimension's base, across a density when one is known, in the target unit.
        var exact = line.Quantity!.MultiplyBy(factor.Value);
        var dimension = unit.Dimension;
        ExactValue across = Rational.One;
        if (IngredientDensities.Find(line.Item) is { } density)
        {
            if (dimension == UnitDimension.Volume && target == UnitSystem.Metric)
            {
                (dimension, across) = (UnitDimension.Weight, (ExactValue)density.GramsPerCup / UnitConversions.CupMilliliters);
            }
            else if (dimension == UnitDimension.Weight && target == UnitSystem.UsCustomary)
            {
                (dimension, across) = (UnitDimension.Volume, (ExactValue)UnitConversions.CupMilliliters / density.GramsPerCup);
            }
        }

        ExactValue inSourceBase = UnitConversions.InBase(unit.Code)!.Value;
        var lowBase = exact.Low!.Value * inSourceBase * across;
        var highBase = exact.High is { } high ? high * inSourceBase * across : (ExactValue?)null;
        var targetUnit = ChooseUnit(target, dimension, lowBase);
        ExactValue inTargetBase = UnitConversions.InBase(targetUnit.Code)!.Value;
        var lowExact = lowBase / inTargetBase;
        var highExact = highBase / inTargetBase;

        var grid = target == UnitSystem.Metric ? RoundingGrid.Metric : RoundingGrid.Kitchen;
        var low = FriendlyRounding.Round(lowExact, grid);
        var roundedHigh = highExact is { } he ? FriendlyRounding.Round(he, grid) : (Rational?)null;
        var rounded = roundedHigh is { } rh && rh != low ? Quantity.Range(low, rh) : Quantity.Amount(low);
        var isRounded = low != lowExact || (highExact is { } eh && (ExactValue)(rounded.High ?? low) != eh);

        var quantityText = RecipeScaler.Write(rounded, grid, text.Substring(quantitySpan.Start, quantitySpan.Length));
        var plural = (rounded.High ?? rounded.Low!.Value) > Rational.One;
        var word = plural ? targetUnit.Plural : targetUnit.Singular;
        var unitText = UnitInflections.IsWord(text.Substring(unitSpan.Start, unitSpan.Length)) ? word : targetUnit.Abbreviation ?? word;
        if (gap.Length == 0 && unitText == word)
        {
            // "250g flour" in cups: a word is never joined to its number.
            gap = " ";
        }

        var converted = new StringBuilder(text.Length + quantityText.Length + unitText.Length)
            .Append(text, 0, quantitySpan.Start)
            .Append(quantityText)
            .Append(gap)
            .Append(unitText)
            .ToString();
        var tail = text[unitSpan.End..];
        var tailMentions = TemperatureMentions.Find(converted + tail, converted.Length);
        var result = ConvertsAll(tailMentions, target)
            ? ReplaceTemperatures(converted + tail, tailMentions)
            : converted + tail;

        var span = new TextSpan(quantitySpan.Start, quantityText.Length);
        if (RecipeScaler.WithinCap(rounded))
        {
            var reread = IngredientLineParser.ParseAnyLength(result);
            if (reread.Quantity != rounded || reread.QuantitySpan != span || reread.UnitCode != targetUnit.Code)
            {
                return asWritten with { Unscaled = UnscaledReason.OtherAmounts };
            }
        }

        return new ScaledIngredientLine(result, rounded, span, isRounded, Unscaled: null, IsConverted: true);
    }

    // The unit an amount reads best in, chosen by its exact value in the base (ml or g) before rounding.
    private static UnitEntry ChooseUnit(UnitSystem target, UnitDimension dimension, ExactValue inBase)
    {
        var code = (target, dimension) switch
        {
            (UnitSystem.Metric, UnitDimension.Volume) => inBase < Thousand ? "milliliter" : "liter",
            (UnitSystem.Metric, _) => inBase < Thousand ? "gram" : "kilogram",
            (_, UnitDimension.Volume) => inBase < TablespoonMilliliters ? "teaspoon" : inBase < QuarterCupMilliliters ? "tablespoon" : "cup",
            _ => inBase < UnitConversions.PoundGrams ? "ounce" : "pound",
        };
        return RecipeScaler.UnitsByCode[code];
    }

    // The temperatures after a line's measure, converted when they all can be; the line otherwise as the scaler shows it.
    private static ScaledIngredientLine WithTemperatures(ScalableIngredientLine line, ScaledIngredientLine scaled, UnitSystem target)
    {
        if (scaled.Unscaled is not null)
        {
            return scaled;
        }

        // Only the quantity and the unit change when a line scales, so everything after them is the blogger's, at the end.
        var unchangedFrom = line.Quantity is { HasAmount: true } ? int.Max(line.QuantitySpan!.Value.End, line.UnitSpan?.End ?? 0) : 0;
        var from = scaled.Text.Length - (line.OriginalText.Length - unchangedFrom);
        var mentions = TemperatureMentions.Find(scaled.Text, from);
        if (!ConvertsAll(mentions, target))
        {
            return scaled;
        }

        var text = ReplaceTemperatures(scaled.Text, mentions);
        var reread = IngredientLineParser.ParseAnyLength(text);
        var original = IngredientLineParser.ParseAnyLength(scaled.Text);
        if (reread.Quantity != original.Quantity || reread.QuantitySpan != original.QuantitySpan || reread.UnitCode != original.UnitCode)
        {
            return scaled;
        }

        return scaled with { Text = text, IsConverted = true };
    }

    // A range that would start below zero ("4-6°F" is -16 to -14 °C) has no way to be written that reads back, so it keeps the
    // text as written; a single temperature below zero is written with its minus sign ("0°F" is "-18°C").
    private static bool ConvertsAll(IReadOnlyList<TemperatureMention> mentions, UnitSystem target) =>
        mentions.Count > 0
        && mentions.All(m => m.Readable && m.UnitCode != TemperatureCode(target) && (m.High is null || Converted(m).Low.Sign >= 0));

    private static (Rational Low, Rational? High) Converted(TemperatureMention mention)
    {
        var scale = mention.UnitCode == Fahrenheit ? UnitSystem.Metric : UnitSystem.UsCustomary;
        var low = RoundTemperature(ConvertTemperature(mention.Low, mention.UnitCode), scale);
        if (mention.High is not { } high)
        {
            return (low, null);
        }

        var roundedHigh = RoundTemperature(ConvertTemperature(high, mention.UnitCode), scale);
        return roundedHigh == low ? (low, null) : (low, roundedHigh);
    }

    private static string NumbersText(TemperatureMention mention, Rational low, Rational? high)
    {
        var lowText = QuantityText.Format(low, QuantityStyle.Fraction);
        return high is not { } h
            ? lowText
            : lowText + (mention.RangeWord is { } to ? " " + to + " " : QuantityText.RangeSeparator) + QuantityText.Format(h, QuantityStyle.Fraction);
    }

    private static string ReplaceTemperatures(string text, IReadOnlyList<TemperatureMention> mentions)
    {
        var converted = new StringBuilder(text.Length + 8);
        var at = 0;
        foreach (var mention in mentions)
        {
            var (low, high) = Converted(mention);
            converted.Append(text, at, mention.Numbers.Start - at)
                .Append(NumbersText(mention, low, high))
                .Append(text, mention.Numbers.End, mention.Unit.Start - mention.Numbers.End)
                .Append(Counterpart(text.Substring(mention.Unit.Start, mention.Unit.Length), mention.UnitCode));
            at = mention.Unit.End;
        }

        return converted.Append(text, at, text.Length - at).ToString();
    }
}
