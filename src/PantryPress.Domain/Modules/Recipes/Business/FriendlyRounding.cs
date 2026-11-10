using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>Which friendly amounts a scaled value rounds to (RCP-004; PP4.7). Chosen by the line's unit and style.</summary>
public enum RoundingGrid
{
    /// <summary>US measures, count units, and lines with no unit, written with fractions.</summary>
    Kitchen,

    /// <summary>The same, for a blogger who writes decimals: no thirds, which no short decimal can show.</summary>
    KitchenDecimal,

    /// <summary>Grams, kilograms, milliliters, deciliters, and liters, always written as decimals.</summary>
    Metric,

    /// <summary>A pinch, a dash, a drop, a handful: a whole number, never a part of one.</summary>
    Whole,
}

/// <summary>
/// Rounds a scaled amount to the nearest friendly one a cook can measure (RCP-004; PP4.7), exactly, with no floating point.
/// The friendly amounts are the multiples of a band's steps, and the band is chosen by the exact value:
/// <list type="table">
/// <item><term>Kitchen</term><description>under ⅛: sixteenths; ⅛ to under 10: eighths and thirds; 10 to under 20: halves;
/// 20 and up: whole numbers.</description></item>
/// <item><term>KitchenDecimal</term><description>the same without thirds.</description></item>
/// <item><term>Metric</term><description>under 1: twentieths (0.05); 1 to under 10: tenths; 10 to under 100: whole
/// numbers; 100 and up: fives.</description></item>
/// <item><term>Whole</term><description>whole numbers.</description></item>
/// </list>
/// The nearest friendly amount wins, a tie goes to the larger, and nothing rounds to zero: a value below the smallest step
/// becomes that step. Each band's lower edge is a friendly amount of that band and of the band below too, so rounding never
/// puts a larger value below a smaller one, and a rounded value rounds to itself. A scaled value's terms are at most
/// <see cref="MaxTerm"/>, a stored term times a factor's (<see cref="ScaleFactor"/>): an input contract that catches a bug.
/// The arithmetic itself is exact at any size (<see cref="ExactValue"/>), because a converted value (PP4.8) - a scaled one
/// times a unit's exact factor - can pass 64 bits before it is rounded.
/// </summary>
public static class FriendlyRounding
{
    /// <summary>The largest numerator or denominator a rounded value may have: the stored cap squared.</summary>
    public const long MaxTerm = RecipeLimits.MaxQuantityTerm * RecipeLimits.MaxQuantityTerm;

    private static readonly Rational Eighth = Rational.Create(1, 8);
    private static readonly Rational Sixteenth = Rational.Create(1, 16);
    private static readonly Rational Third = Rational.Create(1, 3);
    private static readonly Rational OneHalf = Rational.Create(1, 2);
    private static readonly Rational Twentieth = Rational.Create(1, 20);
    private static readonly Rational Tenth = Rational.Create(1, 10);

    private static readonly Band[] KitchenBands =
    [
        new(Eighth, [Sixteenth]),
        new(10, [Eighth, Third]),
        new(20, [OneHalf]),
        new(null, [Rational.One]),
    ];

    private static readonly Band[] KitchenDecimalBands =
    [
        new(Eighth, [Sixteenth]),
        new(10, [Eighth]),
        new(20, [OneHalf]),
        new(null, [Rational.One]),
    ];

    private static readonly Band[] MetricBands =
    [
        new(Rational.One, [Twentieth]),
        new(10, [Tenth]),
        new(100, [Rational.One]),
        new(null, [5]),
    ];

    private static readonly Band[] WholeBands = [new(null, [Rational.One])];

    /// <summary>The friendly amount nearest <paramref name="value"/>, which is above zero.</summary>
    public static Rational Round(Rational value, RoundingGrid grid)
    {
        if (value.Sign <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Only an amount above zero is rounded.");
        }

        if (value.Numerator > MaxTerm || value.Denominator > MaxTerm)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A rounded value's terms are at most a stored term times a factor's.");
        }

        return Round((ExactValue)value, grid);
    }

    /// <summary>
    /// The friendly amount nearest an exact converted value (PP4.8), whose terms may pass 64 bits: a scaled amount times a
    /// unit's exact factor. The rules are the same; only the arithmetic is wider. The rounded value is a multiple of a step
    /// no larger than the value plus one step, so it fits a <see cref="Rational"/> for every amount a recipe can hold.
    /// </summary>
    internal static Rational Round(ExactValue value, RoundingGrid grid)
    {
        if (value.Sign <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Only an amount above zero is rounded.");
        }

        var bands = BandsOf(grid);
        var rounded = Nearest(value, BandFor(value, bands).Steps);
        var smallest = bands[0].Steps.Min();
        return rounded < smallest ? smallest : rounded.ToRational();
    }

    /// <summary>The largest step of the band <paramref name="value"/> falls in: how far apart its friendly amounts are.</summary>
    internal static Rational LargestStep(Rational value, RoundingGrid grid) => BandFor(value, BandsOf(grid)).Steps.Max();

    private static Band[] BandsOf(RoundingGrid grid) => grid switch
    {
        RoundingGrid.Kitchen => KitchenBands,
        RoundingGrid.KitchenDecimal => KitchenDecimalBands,
        RoundingGrid.Metric => MetricBands,
        RoundingGrid.Whole => WholeBands,
        _ => throw new ArgumentOutOfRangeException(nameof(grid), grid, "Not a rounding grid."),
    };

    private static Band BandFor(ExactValue value, Band[] bands) =>
        bands.First(b => b.Below is not { } below || value < below);

    // The multiple of any step nearest the value; a tie goes to the larger. The value is above zero, so every candidate is a
    // whole number of steps: the count below it, and one more.
    private static ExactValue Nearest(ExactValue value, Rational[] steps)
    {
        var best = default(ExactValue);
        var bestDistance = default(ExactValue);
        var found = false;
        foreach (var step in steps)
        {
            ExactValue exactStep = step;
            var below = ExactValue.Whole((value / exactStep).Floor()) * exactStep;
            ExactValue[] candidates = below == value ? [below] : [below, below + exactStep];
            foreach (var candidate in candidates)
            {
                var distance = candidate > value ? candidate - value : value - candidate;
                if (!found || distance < bestDistance || (distance == bestDistance && candidate > best))
                {
                    (best, bestDistance, found) = (candidate, distance, true);
                }
            }
        }

        return best;
    }

    /// <summary>A band: values below <see cref="Below"/> (or every value, for the last) round to multiples of its steps.</summary>
    private sealed record Band(Rational? Below, Rational[] Steps);
}
