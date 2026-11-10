using CsCheck;
using PantryPress.Domain.Modules.Recipes.Business;

namespace PantryPress.Tests.Recipes;

// PP4.7: rounding a scaled amount to a friendly one (RCP-004), as a table a reader can check by eye, then the laws it keeps
// over many generated values: nothing rounds to zero, a larger value never rounds below a smaller one, a rounded value
// rounds to itself, and no value moves more than half a step.
public sealed class FriendlyRoundingTests
{
    private const long Iterations = 20_000;

    private static readonly RoundingGrid[] Grids = Enum.GetValues<RoundingGrid>();

    // Kitchen-sized values, from a speck to a crowd, with denominators that hit the band edges and the steps' midpoints, and
    // now and then a term out to the largest a scaled value can have (a stored term times a factor's).
    private static readonly Gen<Rational> Values = Gen.Select(
        Gen.Frequency((3, Gen.Long[1, 64]), (2, Gen.Long[1, 5_000]), (1, Gen.Long[1, 1_000_000]), (1, Gen.Long[1, FriendlyRounding.MaxTerm])),
        Gen.Frequency((3, Gen.OneOfConst(1L, 2L, 3L, 4L, 6L, 8L, 12L, 16L, 20L, 24L, 32L, 48L, 64L, 100L)), (2, Gen.Long[1, 1_000]), (1, Gen.Long[1, FriendlyRounding.MaxTerm])),
        Rational.Create);

    public static TheoryData<RoundingGrid, long, long, long, long> Examples => new()
    {
        // Kitchen, under 1/8: sixteenths, never below one.
        { RoundingGrid.Kitchen, 1, 16, 1, 16 },
        { RoundingGrid.Kitchen, 1, 100, 1, 16 },
        { RoundingGrid.Kitchen, 3, 64, 1, 16 },
        { RoundingGrid.Kitchen, 3, 32, 1, 8 }, // halfway between 1/16 and 1/8: the larger
        { RoundingGrid.Kitchen, 5, 64, 1, 16 },

        // Kitchen, 1/8 to under 10: eighths and thirds.
        { RoundingGrid.Kitchen, 1, 8, 1, 8 },
        { RoundingGrid.Kitchen, 1, 6, 1, 8 },
        { RoundingGrid.Kitchen, 5, 16, 1, 3 }, // a third is nearer than either eighth
        { RoundingGrid.Kitchen, 3, 16, 1, 4 }, // halfway between 1/8 and 1/4: the larger
        { RoundingGrid.Kitchen, 9, 16, 5, 8 },
        { RoundingGrid.Kitchen, 15, 16, 1, 1 },
        { RoundingGrid.Kitchen, 27, 16, 5, 3 },
        { RoundingGrid.Kitchen, 1, 3, 1, 3 },
        { RoundingGrid.Kitchen, 2, 3, 2, 3 },
        { RoundingGrid.Kitchen, 159, 16, 10, 1 }, // 9 15/16 rounds up to the next band's edge
        { RoundingGrid.Kitchen, 7, 24, 1, 3 }, // halfway between 1/4 and 1/3: the larger
        { RoundingGrid.Kitchen, 17, 48, 3, 8 }, // halfway between 1/3 and 3/8
        { RoundingGrid.Kitchen, 31, 48, 2, 3 }, // halfway between 5/8 and 2/3
        { RoundingGrid.Kitchen, 7, 64, 1, 8 },
        { RoundingGrid.Kitchen, 79, 4, 20, 1 },
        { RoundingGrid.KitchenDecimal, 79, 4, 20, 1 },
        { RoundingGrid.KitchenDecimal, 159, 16, 10, 1 },
        { RoundingGrid.Metric, 39, 40, 1, 1 },
        { RoundingGrid.Metric, 199, 2, 100, 1 },
        { RoundingGrid.Metric, 100, 1, 100, 1 },
        { RoundingGrid.Metric, 205, 2, 105, 1 },
        { RoundingGrid.Metric, 1, 40, 1, 20 },

        // Kitchen, 10 to under 20: halves; 20 and up: whole numbers.
        { RoundingGrid.Kitchen, 10, 1, 10, 1 },
        { RoundingGrid.Kitchen, 45, 4, 23, 2 }, // 11 1/4: halfway, the larger
        { RoundingGrid.Kitchen, 101, 10, 10, 1 },
        { RoundingGrid.Kitchen, 75, 4, 19, 1 },
        { RoundingGrid.Kitchen, 20, 1, 20, 1 },
        { RoundingGrid.Kitchen, 41, 2, 21, 1 },
        { RoundingGrid.Kitchen, 203, 10, 20, 1 },

        // Kitchen for a decimal writer: no thirds.
        { RoundingGrid.KitchenDecimal, 5, 16, 3, 8 },
        { RoundingGrid.KitchenDecimal, 1, 3, 3, 8 },
        { RoundingGrid.KitchenDecimal, 2, 3, 5, 8 },
        { RoundingGrid.KitchenDecimal, 1, 32, 1, 16 },
        { RoundingGrid.KitchenDecimal, 45, 4, 23, 2 },

        // Metric: twentieths under 1, tenths under 10, whole numbers under 100, fives above.
        { RoundingGrid.Metric, 1, 100, 1, 20 },
        { RoundingGrid.Metric, 3, 8, 2, 5 }, // 0.375: halfway between 0.35 and 0.4
        { RoundingGrid.Metric, 9, 16, 11, 20 },
        { RoundingGrid.Metric, 1, 1, 1, 1 },
        { RoundingGrid.Metric, 9, 8, 11, 10 },
        { RoundingGrid.Metric, 5, 4, 13, 10 },
        { RoundingGrid.Metric, 15, 4, 19, 5 },
        { RoundingGrid.Metric, 399, 40, 10, 1 },
        { RoundingGrid.Metric, 25, 2, 13, 1 },
        { RoundingGrid.Metric, 375, 4, 94, 1 },
        { RoundingGrid.Metric, 375, 2, 190, 1 },
        { RoundingGrid.Metric, 625, 2, 315, 1 },
        { RoundingGrid.Metric, 1001, 1, 1000, 1 },

        // Whole: inexact measures, never below one.
        { RoundingGrid.Whole, 1, 2, 1, 1 },
        { RoundingGrid.Whole, 1, 100, 1, 1 },
        { RoundingGrid.Whole, 3, 2, 2, 1 },
        { RoundingGrid.Whole, 5, 4, 1, 1 },
        { RoundingGrid.Whole, 15, 4, 4, 1 },
    };

    [Theory]
    [MemberData(nameof(Examples))]
    public void Rounds_to_the_nearest_friendly_amount(RoundingGrid grid, long n, long d, long expectedN, long expectedD) =>
        Assert.Equal(Rational.Create(expectedN, expectedD), FriendlyRounding.Round(Rational.Create(n, d), grid));

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Only_an_amount_above_zero_is_rounded(long value)
    {
        foreach (var grid in Grids)
        {
            Assert.Equal("value", Assert.Throws<ArgumentOutOfRangeException>(() => FriendlyRounding.Round(value, grid)).ParamName);
        }
    }

    [Fact]
    public void A_term_past_a_stored_term_times_a_factors_is_refused()
    {
        Assert.Equal(FriendlyRounding.MaxTerm, FriendlyRounding.Round(FriendlyRounding.MaxTerm, RoundingGrid.Metric).Numerator);
        Assert.Equal(Rational.Create(1, 16), FriendlyRounding.Round(Rational.Create(1, FriendlyRounding.MaxTerm), RoundingGrid.Kitchen));
        foreach (var grid in Grids)
        {
            Assert.Equal("value", Assert.Throws<ArgumentOutOfRangeException>(() => FriendlyRounding.Round(FriendlyRounding.MaxTerm + 1, grid)).ParamName);
            Assert.Equal("value", Assert.Throws<ArgumentOutOfRangeException>(() => FriendlyRounding.Round(Rational.Create(1, FriendlyRounding.MaxTerm + 1), grid)).ParamName);
        }
    }

    [Fact]
    public void An_unknown_grid_is_refused() =>
        Assert.Equal("grid", Assert.Throws<ArgumentOutOfRangeException>(() => FriendlyRounding.Round(1, (RoundingGrid)99)).ParamName);

    [Fact]
    public void Nothing_rounds_below_the_smallest_step() =>
        Gen.Select(Values, Gen.OneOfConst(Grids)).Sample(iter: Iterations, assert: (value, grid) =>
            Assert.True(FriendlyRounding.Round(value, grid) >= Smallest(grid)));

    [Fact]
    public void A_rounded_amount_rounds_to_itself() =>
        Gen.Select(Values, Gen.OneOfConst(Grids)).Sample(iter: Iterations, assert: (value, grid) =>
        {
            var rounded = FriendlyRounding.Round(value, grid);
            Assert.Equal(rounded, FriendlyRounding.Round(rounded, grid));
        });

    [Fact]
    public void A_larger_amount_never_rounds_below_a_smaller_one() =>
        Gen.Select(Values, Values, Gen.OneOfConst(Grids)).Sample(iter: Iterations, assert: (a, b, grid) =>
        {
            var (low, high) = a <= b ? (a, b) : (b, a);
            Assert.True(FriendlyRounding.Round(low, grid) <= FriendlyRounding.Round(high, grid));
        });

    // Within half the band's largest step of the exact amount, or the smallest step for a speck below it.
    [Fact]
    public void No_amount_moves_more_than_half_a_step() =>
        Gen.Select(Values, Gen.OneOfConst(Grids)).Sample(iter: Iterations, assert: (value, grid) =>
        {
            var rounded = FriendlyRounding.Round(value, grid);
            var distance = rounded > value ? rounded - value : value - rounded;
            var half = FriendlyRounding.LargestStep(value, grid) * Rational.Create(1, 2);
            Assert.True(distance <= half || (rounded == Smallest(grid) && value < rounded), $"{value} rounded to {rounded} on {grid}");
        });

    // The amounts each grid can land on, read back from the text a reader sees: metric amounts are exact short decimals,
    // and kitchen amounts below 10 have a sixteenth, an eighth, or a third as their fraction.
    [Fact]
    public void Every_rounded_amount_is_one_a_cook_can_measure() =>
        Gen.Select(Values, Gen.OneOfConst(Grids)).Sample(iter: Iterations, assert: (value, grid) =>
        {
            var rounded = FriendlyRounding.Round(value, grid);
            var allowed = grid switch
            {
                RoundingGrid.Kitchen => new long[] { 1, 2, 3, 4, 8, 16 },
                RoundingGrid.KitchenDecimal => [1, 2, 4, 8, 16],
                RoundingGrid.Metric => [1, 2, 4, 5, 10, 20],
                _ => [1],
            };
            Assert.Contains(rounded.Denominator, allowed);
        });

    private static Rational Smallest(RoundingGrid grid) => grid switch
    {
        RoundingGrid.Kitchen or RoundingGrid.KitchenDecimal => Rational.Create(1, 16),
        RoundingGrid.Metric => Rational.Create(1, 20),
        _ => Rational.One,
    };
}
