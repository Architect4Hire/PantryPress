using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.2: the display text of a quantity (RCP-004), as a table a reader can check by eye. The text is exact: rounding to
// friendly fractions is PP4.7's, so a sixteenth stays a sixteenth here.
public sealed class QuantityTextTests
{
    public static TheoryData<long, long, string> FractionStyle => new()
    {
        { 1, 2, "½" },
        { 1, 3, "⅓" },
        { 2, 3, "⅔" },
        { 1, 4, "¼" },
        { 3, 4, "¾" },
        { 1, 8, "⅛" },
        { 3, 8, "⅜" },
        { 5, 8, "⅝" },
        { 7, 8, "⅞" },
        { 3, 2, "1½" },
        { 7, 4, "1¾" },
        { 5, 3, "1⅔" },
        { 23, 8, "2⅞" },
        { 2, 1, "2" },
        { 1000, 1, "1000" },
        { 1_234_567, 1, "1234567" },
        { 5, 16, "5/16" },
        { 21, 16, "1 5/16" },
        { 2, 5, "2/5" },
        { 1, 6, "1/6" },
        { 7, 6, "1 1/6" },
        { 1, 10, "1/10" },
        { 0, 1, "0" },
        { -3, 2, "-1½" },
        { -5, 16, "-5/16" },
    };

    [Theory]
    [MemberData(nameof(FractionStyle))]
    public void Fraction_style_writes_a_mixed_number_with_kitchen_fractions(long n, long d, string text) =>
        Assert.Equal(text, QuantityText.Format(Rational.Create(n, d), QuantityStyle.Fraction));

    public static TheoryData<long, long, string> DecimalStyle => new()
    {
        { 1, 2, "0.5" },
        { 3, 2, "1.5" },
        { 5, 4, "1.25" },
        { 1, 8, "0.125" },
        { 21, 20, "1.05" },
        { 3, 1000, "0.003" },
        { 1001, 1000, "1.001" },
        { 250, 1, "250" },
        { 12_345, 1, "12345" },
        { 0, 1, "0" },
        { -1, 4, "-0.25" },

        // Not exact within three places, so written as a fraction rather than rounded.
        { 1, 3, "⅓" },
        { 4, 3, "1⅓" },
        { 1, 16, "1/16" },
        { 1, 6, "1/6" },
        { 1, 2000, "1/2000" },
        { -2, 3, "-⅔" },
    };

    [Theory]
    [MemberData(nameof(DecimalStyle))]
    public void Decimal_style_writes_exact_decimals_and_fractions_otherwise(long n, long d, string text) =>
        Assert.Equal(text, QuantityText.Format(Rational.Create(n, d), QuantityStyle.Decimal));

    public static TheoryData<long, long, long, long, QuantityStyle, string> Ranges => new()
    {
        { 2, 1, 3, 1, QuantityStyle.Fraction, "2–3" },
        { 3, 2, 2, 1, QuantityStyle.Fraction, "1½–2" },
        { 1, 4, 1, 3, QuantityStyle.Fraction, "¼–⅓" },
        { 1, 2, 3, 4, QuantityStyle.Decimal, "0.5–0.75" },
        { 2, 1, 3, 1, QuantityStyle.Decimal, "2–3" },
        { 3, 2, 2, 1, QuantityStyle.Decimal, "1.5–2" },
        { 1, 8, 1, 4, QuantityStyle.Decimal, "0.125–0.25" },
        { 7, 6, 4, 3, QuantityStyle.Fraction, "1 1/6–1⅓" },

        // One end cannot be a decimal, so neither is: a range never mixes "0.5" with "⅔".
        { 1, 2, 2, 3, QuantityStyle.Decimal, "½–⅔" },
        { 1, 3, 1, 2, QuantityStyle.Decimal, "⅓–½" },
        { 1, 4, 5, 16, QuantityStyle.Decimal, "¼–5/16" },
    };

    [Theory]
    [MemberData(nameof(Ranges))]
    public void A_range_is_both_ends_joined_by_an_en_dash(long lowN, long lowD, long highN, long highD, QuantityStyle style, string text) =>
        Assert.Equal(text, QuantityText.Format(Quantity.Range(Rational.Create(lowN, lowD), Rational.Create(highN, highD)), style));

    [Theory]
    [InlineData(QuantityStyle.Fraction)]
    [InlineData(QuantityStyle.Decimal)]
    public void To_taste_is_written_as_to_taste(QuantityStyle style) =>
        Assert.Equal("to taste", QuantityText.Format(Quantity.ToTaste, style));

    [Theory]
    [InlineData(QuantityStyle.Fraction, "1½")]
    [InlineData(QuantityStyle.Decimal, "1.5")]
    public void An_amount_is_its_value(QuantityStyle style, string text) =>
        Assert.Equal(text, QuantityText.Format(Quantity.Amount(Rational.Create(3, 2)), style));

    [Fact]
    public void The_range_separator_is_an_en_dash() => Assert.Equal("–", QuantityText.RangeSeparator);

    // The largest values a Rational holds are written exactly in both styles: the whole part is worked in 128 bits and
    // only the remainder is scaled to decimal places, so nothing overflows and the most negative value has a spelling.
    public static TheoryData<long, long, string, string> Extremes => new()
    {
        { long.MaxValue, 1, "9223372036854775807", "9223372036854775807" },
        { long.MaxValue, 2, "4611686018427387903½", "4611686018427387903.5" },
        { long.MaxValue, 1000, "9223372036854775 807/1000", "9223372036854775.807" },
        { long.MinValue, 1, "-9223372036854775808", "-9223372036854775808" },
        { long.MinValue + 1, 8, "-1152921504606846975⅞", "-1152921504606846975.875" },
        { 1, long.MaxValue, "1/9223372036854775807", "1/9223372036854775807" },
    };

    [Theory]
    [MemberData(nameof(Extremes))]
    public void The_largest_values_are_written_exactly(long n, long d, string fraction, string @decimal)
    {
        var value = Rational.Create(n, d);

        Assert.Equal(fraction, QuantityText.Format(value, QuantityStyle.Fraction));
        Assert.Equal(@decimal, QuantityText.Format(value, QuantityStyle.Decimal));
    }

    public static TheoryData<Quantity> EveryKind => new() { Quantity.Amount(1), Quantity.Range(1, 2), Quantity.ToTaste };

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void An_unknown_style_is_refused_for_every_kind(Quantity quantity)
    {
        Assert.Equal("style", Assert.Throws<ArgumentOutOfRangeException>(() => QuantityText.Format(quantity, (QuantityStyle)99)).ParamName);
        Assert.Equal("style", Assert.Throws<ArgumentOutOfRangeException>(() => QuantityText.Format(Rational.One, (QuantityStyle)99)).ParamName);
    }

    [Fact]
    public void A_missing_quantity_is_refused() =>
        Assert.Throws<ArgumentNullException>(() => QuantityText.Format(null!, QuantityStyle.Fraction));
}
