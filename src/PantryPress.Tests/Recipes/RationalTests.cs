using PantryPress.Domain.Modules.Recipes.Business;

namespace PantryPress.Tests.Recipes;

// PP4.2: the exact fraction every recipe quantity is made of (PP-D05). Laws over many values are in
// QuantityPropertyTests; these are the edges a generator would rarely reach.
public sealed class RationalTests
{
    [Theory]
    [InlineData(2, 4, 1, 2)]
    [InlineData(-2, -4, 1, 2)]
    [InlineData(2, -4, -1, 2)]
    [InlineData(-6, 4, -3, 2)]
    [InlineData(0, 5, 0, 1)]
    [InlineData(0, -5, 0, 1)]
    [InlineData(12, 3, 4, 1)]
    [InlineData(7, 1, 7, 1)]
    public void A_fraction_is_kept_in_lowest_terms_with_a_positive_denominator(long n, long d, long numerator, long denominator)
    {
        var value = Rational.Create(n, d);

        Assert.Equal(numerator, value.Numerator);
        Assert.Equal(denominator, value.Denominator);
    }

    [Fact]
    public void The_default_value_is_zero_over_one()
    {
        var value = default(Rational);

        Assert.Equal(0, value.Numerator);
        Assert.Equal(1, value.Denominator);
        Assert.Equal(Rational.Zero, value);
        Assert.Equal(Rational.Create(0, 7), value);
        Assert.True(value.IsInteger);
    }

    [Fact]
    public void Equal_values_are_equal_however_they_were_written()
    {
        Assert.Equal(Rational.Create(1, 2), Rational.Create(50, 100));
        Assert.Equal(Rational.Create(1, 2).GetHashCode(), Rational.Create(50, 100).GetHashCode());
        Assert.Equal((Rational)3, Rational.Create(9, 3));
        Assert.NotEqual(Rational.Create(1, 2), Rational.Create(-1, 2));
    }

    [Fact]
    public void Arithmetic_is_exact()
    {
        Assert.Equal(Rational.Create(5, 6), Rational.Create(1, 2) + Rational.Create(1, 3));
        Assert.Equal(Rational.Create(1, 6), Rational.Create(1, 2) - Rational.Create(1, 3));
        Assert.Equal(Rational.Create(1, 6), Rational.Create(1, 2) * Rational.Create(1, 3));
        Assert.Equal(Rational.Create(3, 2), Rational.Create(1, 2) / Rational.Create(1, 3));
        Assert.Equal(Rational.Create(-1, 3), -Rational.Create(1, 3));
        Assert.Equal(Rational.Create(3, 2), Rational.Create(2, 3).Reciprocal());
        Assert.Equal(Rational.Create(-3, 2), Rational.Create(-2, 3).Reciprocal());

        // A third added three times is one: the case binary floating point gets wrong.
        var third = Rational.Create(1, 3);
        Assert.Equal(Rational.One, third + third + third);
        Assert.Equal(Rational.Create(3, 10), Rational.Create(1, 10) + Rational.Create(2, 10));
    }

    [Fact]
    public void A_negative_denominator_moves_its_sign_to_the_numerator()
    {
        Assert.Equal(Rational.Create(-3, 2), Rational.Create(1, 2) / Rational.Create(-1, 3));
        Assert.Equal(-1, Rational.Create(2, long.MinValue).Numerator);
        Assert.Equal(4_611_686_018_427_387_904, Rational.Create(2, long.MinValue).Denominator);
    }

    [Fact]
    public void Nothing_is_divided_by_zero()
    {
        Assert.Throws<DivideByZeroException>(() => Rational.Create(1, 0));
        Assert.Throws<DivideByZeroException>(() => Rational.Create(0, 0));
        Assert.Throws<DivideByZeroException>(() => Rational.One / Rational.Zero);
        Assert.Throws<DivideByZeroException>(() => Rational.Zero.Reciprocal());
    }

    // Lowest terms that do not fit in 64 bits throw rather than wrap or round.
    [Fact]
    public void A_result_too_large_to_hold_throws()
    {
        Assert.Throws<OverflowException>(() => Rational.Create(long.MinValue, -1));
        Assert.Throws<OverflowException>(() => Rational.Create(1, long.MinValue));
        Assert.Throws<OverflowException>(() => (Rational)long.MinValue / -1);
        Assert.Throws<OverflowException>(() => -(Rational)long.MinValue);
        Assert.Throws<OverflowException>(() => (Rational)long.MaxValue + 1);
        Assert.Throws<OverflowException>(() => (Rational)long.MaxValue * 2);
        Assert.Throws<OverflowException>(() => Rational.Create(1, long.MaxValue) * Rational.Create(1, 2));
        Assert.Throws<OverflowException>(() => Rational.Create(1, long.MaxValue) + Rational.Create(1, long.MaxValue - 1));
    }

    // The work is done in 128 bits and reduced before it is checked, so a large intermediate is not an overflow.
    [Fact]
    public void A_large_intermediate_that_reduces_is_not_an_overflow()
    {
        Assert.Equal((Rational)long.MaxValue, Rational.Create(long.MaxValue, 2) * 2);
        Assert.Equal(Rational.One, Rational.Create(long.MaxValue, long.MaxValue - 1) * Rational.Create(long.MaxValue - 1, long.MaxValue));
        Assert.Equal(Rational.Zero, (Rational)long.MaxValue - long.MaxValue);
        Assert.Equal((Rational)long.MinValue, (Rational)(long.MinValue + 1) - 1);
    }

    [Fact]
    public void Comparison_is_exact_at_the_edges_of_the_range()
    {
        var justOverOne = Rational.Create(long.MaxValue, long.MaxValue - 1);
        var justUnderOne = Rational.Create(long.MaxValue - 1, long.MaxValue);

        Assert.True(justOverOne > Rational.One);
        Assert.True(justUnderOne < Rational.One);
        Assert.True(justUnderOne < justOverOne);
        Assert.True((Rational)long.MinValue < long.MaxValue);
        Assert.True(Rational.Create(1, 3) <= Rational.Create(2, 6));
        Assert.True(Rational.Create(1, 3) >= Rational.Create(2, 6));
        Assert.Equal(0, Rational.Create(1, 3).CompareTo(Rational.Create(2, 6)));
    }

    [Theory]
    [InlineData(1, 2, 1)]
    [InlineData(0, 1, 0)]
    [InlineData(-1, 2, -1)]
    public void Sign_is_the_numerator_sign(long n, long d, int sign) => Assert.Equal(sign, Rational.Create(n, d).Sign);

    [Theory]
    [InlineData(1, 2, "1/2")]
    [InlineData(-1, 2, "-1/2")]
    [InlineData(6, 2, "3")]
    [InlineData(1_234_567, 1, "1234567")]
    public void ToString_is_the_plain_fraction(long n, long d, string text) => Assert.Equal(text, Rational.Create(n, d).ToString());
}
