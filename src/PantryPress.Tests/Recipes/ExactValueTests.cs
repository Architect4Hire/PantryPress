using System.Numerics;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.8: the exact fraction with no size limit that conversion rounds from. Every expectation is worked by hand, so none
// leans on ExactValue to check itself.
public sealed class ExactValueTests
{
    private static readonly BigInteger TenToTwenty = BigInteger.Pow(10, 20);

    [Fact]
    public void A_value_is_in_lowest_terms_with_a_positive_denominator()
    {
        var value = ExactValue.Whole(6) / ExactValue.Whole(-4);
        Assert.Equal(new BigInteger(-3), value.Numerator);
        Assert.Equal(new BigInteger(2), value.Denominator);
        Assert.Equal(ExactValue.Whole(-3) / ExactValue.Whole(2), value);
        Assert.Equal(value.GetHashCode(), (ExactValue.Whole(-3) / ExactValue.Whole(2)).GetHashCode());
        Assert.Equal(-1, value.Sign);
    }

    [Fact]
    public void Default_is_zero()
    {
        Assert.Equal(ExactValue.Whole(0), default);
        Assert.Equal(BigInteger.One, default(ExactValue).Denominator);
        Assert.Equal(0, default(ExactValue).Sign);
        Assert.Equal("0", default(ExactValue).ToString());
    }

    [Fact]
    public void A_rational_converts_exactly()
    {
        ExactValue value = Rational.Create(157_725_491, 32_000_000);
        Assert.Equal(new BigInteger(157_725_491), value.Numerator);
        Assert.Equal(new BigInteger(32_000_000), value.Denominator);
        Assert.Equal(Rational.Create(157_725_491, 32_000_000), value.ToRational());
    }

    [Fact]
    public void Arithmetic_is_exact_past_sixty_four_bits()
    {
        ExactValue big = Rational.Create(RecipeLimits.MaxQuantityTerm * RecipeLimits.MaxQuantityTerm, 1);
        var product = big * big * big;
        Assert.Equal(BigInteger.Pow(10, 36), product.Numerator);
        Assert.Equal(BigInteger.One, (product / product).Numerator);
        Assert.Equal(ExactValue.Whole(TenToTwenty + 1), ExactValue.Whole(TenToTwenty) + ExactValue.Whole(1));
        Assert.Equal(ExactValue.Whole(TenToTwenty - 1), ExactValue.Whole(TenToTwenty) - ExactValue.Whole(1));
        Assert.True(ExactValue.Whole(TenToTwenty) > ExactValue.Whole(long.MaxValue));
        Assert.True(ExactValue.Whole(TenToTwenty) / ExactValue.Whole(TenToTwenty + 1) < ExactValue.Whole(1));
        Assert.True(ExactValue.Whole(1) <= ExactValue.Whole(TenToTwenty + 1) / ExactValue.Whole(TenToTwenty));
        Assert.Equal(0, (ExactValue.Whole(2) / ExactValue.Whole(4)).CompareTo(Rational.Create(1, 2)));
    }

    [Theory]
    [InlineData(7, 2, 3)]
    [InlineData(-7, 2, -4)]
    [InlineData(-1, 2, -1)]
    [InlineData(1, 2, 0)]
    [InlineData(6, 3, 2)]
    [InlineData(-6, 3, -2)]
    [InlineData(0, 5, 0)]
    public void Floor_is_the_largest_whole_number_at_or_below(long numerator, long denominator, long expected) =>
        Assert.Equal(new BigInteger(expected), (ExactValue.Whole(numerator) / ExactValue.Whole(denominator)).Floor());

    [Fact]
    public void A_value_past_sixty_four_bits_is_no_rational()
    {
        Assert.Throws<OverflowException>(() => ExactValue.Whole(TenToTwenty).ToRational());
        Assert.Throws<OverflowException>(() => (ExactValue.Whole(1) / ExactValue.Whole(TenToTwenty)).ToRational());
        Assert.Equal((Rational)long.MaxValue, ExactValue.Whole(long.MaxValue).ToRational());
    }

    [Fact]
    public void Dividing_by_zero_throws()
    {
        Assert.Throws<DivideByZeroException>(() => ExactValue.Whole(1) / ExactValue.Whole(0));
        Assert.Throws<DivideByZeroException>(() => ExactValue.Whole(1) / default(ExactValue));
    }

    // The wide overload rounds values whose terms pass 64 bits, by the same bands as the narrow one.
    [Fact]
    public void Rounding_a_value_with_terms_past_sixty_four_bits()
    {
        // 1 + 10^-20 and 100 + 10^-18: just above a band's edge, each the edge.
        var justAboveOne = ExactValue.Whole(TenToTwenty + 1) / ExactValue.Whole(TenToTwenty);
        Assert.Equal(Rational.One, FriendlyRounding.Round(justAboveOne, RoundingGrid.Metric));
        Assert.Equal(Rational.One, FriendlyRounding.Round(justAboveOne, RoundingGrid.Kitchen));
        var justAboveHundred = ExactValue.Whole(TenToTwenty + 1) / ExactValue.Whole(BigInteger.Pow(10, 18));
        Assert.Equal((Rational)100, FriendlyRounding.Round(justAboveHundred, RoundingGrid.Metric));

        // 3.06 with 10^20 in the denominator: the kitchen grid's nearest is 3 (3⅛ is 0.065 away).
        var threeish = ExactValue.Whole((306 * TenToTwenty / 100) + 1) / ExactValue.Whole(TenToTwenty);
        Assert.Equal((Rational)3, FriendlyRounding.Round(threeish, RoundingGrid.Kitchen));

        // 10^-20 rounds up to the smallest step, never to zero.
        Assert.Equal(Rational.Create(1, 16), FriendlyRounding.Round(ExactValue.Whole(1) / ExactValue.Whole(TenToTwenty), RoundingGrid.Kitchen));

        // A rounded value past 64 bits is a bug, and throws rather than wraps.
        Assert.Throws<OverflowException>(() => FriendlyRounding.Round(ExactValue.Whole(TenToTwenty), RoundingGrid.Whole));
        Assert.Throws<ArgumentOutOfRangeException>(() => FriendlyRounding.Round(default(ExactValue), RoundingGrid.Whole));
    }
}
