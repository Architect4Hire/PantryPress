using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using CsCheck;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.2: the laws quantities keep over many generated values (recipes.md: "scaling by k then 1/k returns the original
// quantity"). CsCheck samples fresh values each run and, on a failure, shrinks it and prints the seed that replays it.
public sealed partial class QuantityPropertyTests
{
    // Many samples per law: each one is a handful of integer operations.
    private const long Iterations = 10_000;

    // Kitchen-sized values; a product of three still fits in 64 bits after reduction. A share of the numerators is small,
    // and half the denominators are small or divide 1,000, so a lone kitchen fraction (⅜), a proper ASCII one (5/16), a
    // decimal below one (0.125), and the exact decimals are drawn often, not by luck.
    private static readonly Gen<long> Numerators = Gen.Frequency((3, Gen.Long[-32, 32]), (5, Gen.Long[-10_000, 10_000]));
    private static readonly Gen<long> Denominators = Gen.Frequency(
        (3, Gen.Long[1, 16]),
        (1, Gen.OneOfConst(20L, 25L, 40L, 50L, 100L, 125L, 200L, 250L, 500L, 1_000L)),
        (4, Gen.Long[1, 10_000]));
    private static readonly Gen<Rational> Rationals = Gen.Select(Numerators, Denominators, Rational.Create);
    private static readonly Gen<Rational> NonZero = Rationals.Where(r => r.Sign != 0);
    private static readonly Gen<Rational> Positive =
        Gen.Select(Gen.Frequency((3, Gen.Long[1, 32]), (5, Gen.Long[1, 10_000])), Denominators, Rational.Create);

    // The whole 64-bit range, weighted toward its edges, where the 128-bit work and the checked narrowing matter.
    private static readonly Gen<long> WideLongs = Gen.Frequency(
        (1, Gen.OneOfConst(long.MinValue, long.MinValue + 1, long.MinValue / 2, -2L, -1L, 0L, 1L, 2L, long.MaxValue / 2, long.MaxValue - 1, long.MaxValue)),
        (1, Gen.Long[-1_000, 1_000]),
        (2, Gen.Long));
    private static readonly Gen<Rational> WideRationals =
        Gen.Select(WideLongs, WideLongs.Where(d => d > 0), Rational.Create);
    private static readonly Gen<Rational> Factors = Gen.Select(Gen.Long[1, 1_000], Gen.Long[1, 1_000], Rational.Create);

    private static readonly Gen<Quantity> Quantities = Gen.Frequency(
        (4, Positive.Select(Quantity.Amount)),
        (4, Gen.Select(Positive, Positive, (low, rise) => Quantity.Range(low, low + rise))),
        (1, Gen.Const(Quantity.ToTaste)));

    // The kitchen fractions QuantityText writes as one character, and their values.
    private static readonly Dictionary<char, Rational> Glyphs = new()
    {
        ['½'] = Rational.Create(1, 2),
        ['⅓'] = Rational.Create(1, 3),
        ['⅔'] = Rational.Create(2, 3),
        ['¼'] = Rational.Create(1, 4),
        ['¾'] = Rational.Create(3, 4),
        ['⅛'] = Rational.Create(1, 8),
        ['⅜'] = Rational.Create(3, 8),
        ['⅝'] = Rational.Create(5, 8),
        ['⅞'] = Rational.Create(7, 8),
    };

    [Fact]
    public void A_fraction_is_in_lowest_terms_and_keeps_its_value() =>
        Gen.Select(Numerators, Denominators.Select(d => d * (d % 2 == 0 ? 1 : -1))).Sample(iter: Iterations, assert: (n, d) =>
        {
            var value = Rational.Create(n, d);

            Assert.True(value.Denominator > 0);
            Assert.Equal(1, Gcd(Math.Abs(value.Numerator), value.Denominator));
            Assert.Equal((Int128)n * value.Denominator, (Int128)value.Numerator * d);
        });

    // The arithmetic checked against BigInteger over the whole range: each result is the exact fraction in lowest terms
    // when that fits in 64 bits, and an OverflowException exactly when it does not.
    [Fact]
    public void Creating_a_fraction_matches_exact_arithmetic_or_overflows_only_when_it_cannot_fit() =>
        Gen.Select(WideLongs, WideLongs.Where(d => d != 0)).Sample(iter: Iterations, assert: (n, d) =>
            AssertExact(() => Rational.Create(n, d), n, d));

    [Fact]
    public void Arithmetic_matches_exact_arithmetic_or_overflows_only_when_it_cannot_fit() =>
        Gen.Select(WideRationals, WideRationals).Sample(iter: Iterations, assert: (a, b) =>
        {
            BigInteger an = a.Numerator, ad = a.Denominator, bn = b.Numerator, bd = b.Denominator;

            AssertExact(() => a + b, (an * bd) + (bn * ad), ad * bd);
            AssertExact(() => a - b, (an * bd) - (bn * ad), ad * bd);
            AssertExact(() => a * b, an * bn, ad * bd);
            AssertExact(() => -a, -an, ad);
            if (b.Sign != 0)
            {
                AssertExact(() => a / b, an * bd, ad * bn);
            }

            Assert.Equal((an * bd).CompareTo(bn * ad), a.CompareTo(b));
        });

    [Fact]
    public void Every_value_is_written_exactly_across_the_whole_range() =>
        Gen.Select(WideRationals, Gen.OneOfConst(QuantityStyle.Fraction, QuantityStyle.Decimal)).Sample(iter: Iterations, assert: (value, style) =>
            Assert.Equal(((BigInteger)value.Numerator, (BigInteger)value.Denominator), ReadExact(QuantityText.Format(value, style))));

    [Fact]
    public void Equal_quantities_are_equal_and_hash_alike() =>
        Gen.Select(Quantities, Gen.Long[1, 1_000]).Sample(iter: Iterations, assert: (quantity, k) =>
        {
            Rational Unreduced(Rational? value) => Rational.Create(value!.Value.Numerator * k, value.Value.Denominator * k);
            var rebuilt = quantity.Kind switch
            {
                QuantityKind.Amount => Quantity.Amount(Unreduced(quantity.Low)),
                QuantityKind.Range => Quantity.Range(Unreduced(quantity.Low), Unreduced(quantity.High)),
                _ => Quantity.ToTaste,
            };

            Assert.Equal(quantity, rebuilt);
            Assert.True(quantity == rebuilt);
            Assert.Equal(quantity.GetHashCode(), rebuilt.GetHashCode());
        });

    [Fact]
    public void Equal_values_are_equal_and_hash_alike() =>
        Gen.Select(Rationals, Gen.Long[1, 1_000]).Sample(iter: Iterations, assert: (value, k) =>
        {
            var scaledUp = Rational.Create(value.Numerator * k, value.Denominator * k);

            Assert.Equal(value, scaledUp);
            Assert.Equal(value.GetHashCode(), scaledUp.GetHashCode());
            Assert.Equal(0, value.CompareTo(scaledUp));
        });

    [Fact]
    public void Addition_and_multiplication_commute_and_associate() =>
        Gen.Select(Rationals, Rationals, Rationals).Sample(iter: Iterations, assert: (a, b, c) =>
        {
            Assert.Equal(a + b, b + a);
            Assert.Equal(a * b, b * a);
            Assert.Equal((a + b) + c, a + (b + c));
            Assert.Equal((a * b) * c, a * (b * c));
        });

    [Fact]
    public void Multiplication_distributes_over_addition() =>
        Gen.Select(Rationals, Rationals, Rationals).Sample(iter: Iterations, assert: (a, b, c) => Assert.Equal((a * b) + (a * c), a * (b + c)));

    [Fact]
    public void Subtraction_and_division_undo_addition_and_multiplication() =>
        Gen.Select(Rationals, NonZero).Sample(iter: Iterations, assert: (a, b) =>
        {
            Assert.Equal(a, a + b - b);
            Assert.Equal(a, a * b / b);
            Assert.Equal(a, a / b * b);
            Assert.Equal(Rational.Zero, a - a);
            Assert.Equal(Rational.Zero, a + -a);
            Assert.Equal(Rational.One, b * b.Reciprocal());
        });

    [Fact]
    public void Comparison_agrees_with_cross_multiplication() =>
        Gen.Select(Rationals, Rationals).Sample(iter: Iterations, assert: (a, b) =>
        {
            var expected = ((Int128)a.Numerator * b.Denominator).CompareTo((Int128)b.Numerator * a.Denominator);

            Assert.Equal(expected, a.CompareTo(b));
            Assert.Equal(-expected, b.CompareTo(a));
            Assert.Equal(expected == 0, a == b);
            Assert.Equal(expected < 0, a < b);
        });

    [Fact]
    public void Scaling_by_k_then_one_over_k_returns_the_original() =>
        Gen.Select(Quantities, Factors).Sample(iter: Iterations, assert: (quantity, k) =>
            Assert.Equal(quantity, quantity.MultiplyBy(k).MultiplyBy(k.Reciprocal())));

    [Fact]
    public void Scaling_twice_is_scaling_once_by_the_product() =>
        Gen.Select(Quantities, Factors, Factors).Sample(iter: Iterations, assert: (quantity, k, m) =>
            Assert.Equal(quantity.MultiplyBy(k * m), quantity.MultiplyBy(k).MultiplyBy(m)));

    [Fact]
    public void Scaling_keeps_the_kind_and_a_range_in_order() =>
        Gen.Select(Quantities, Factors).Sample(iter: Iterations, assert: (quantity, k) =>
        {
            var scaled = quantity.MultiplyBy(k);

            Assert.Equal(quantity.Kind, scaled.Kind);
            Assert.Equal(quantity, quantity.MultiplyBy(Rational.One));
            if (scaled.Kind == QuantityKind.Range)
            {
                Assert.True(scaled.Low < scaled.High);
            }
        });

    [Fact]
    public void Display_text_reads_back_as_the_same_quantity_in_both_styles() =>
        Gen.Select(Quantities, Gen.OneOfConst(QuantityStyle.Fraction, QuantityStyle.Decimal)).Sample(iter: Iterations, assert: (quantity, style) =>
            Assert.Equal(quantity, Read(QuantityText.Format(quantity, style))));

    [Fact]
    public void Display_text_reads_back_as_the_same_value_whatever_its_sign() =>
        Gen.Select(Rationals, Gen.OneOfConst(QuantityStyle.Fraction, QuantityStyle.Decimal)).Sample(iter: Iterations, assert: (value, style) =>
            Assert.Equal(value, ReadValue(QuantityText.Format(value, style))));

    [Fact]
    public void Fraction_style_is_a_whole_number_a_kitchen_fraction_or_a_proper_ascii_fraction() =>
        Rationals.Sample(iter: Iterations, assert: value =>
        {
            var text = QuantityText.Format(value, QuantityStyle.Fraction);

            Assert.Matches(FractionShape(), text);
            var glyph = Glyphs.Values.Contains(Remainder(value));
            Assert.Equal(glyph, text.Length > 0 && Glyphs.ContainsKey(text[^1]));
        });

    [Fact]
    public void Decimal_style_is_a_decimal_exactly_when_one_ends_within_three_places() =>
        Rationals.Sample(iter: Iterations, assert: value =>
        {
            var text = QuantityText.Format(value, QuantityStyle.Decimal);

            if (1_000 % value.Denominator == 0)
            {
                Assert.Matches(DecimalShape(), text);
            }
            else
            {
                Assert.Equal(QuantityText.Format(value, QuantityStyle.Fraction), text);
            }
        });

    // The text never reads the culture: a comma decimal point, a non-ASCII minus, or digit grouping would all show here.
    // The baseline is written under the invariant culture, so the test means the same on a machine set to any of these.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("sv-SE")]
    [InlineData("fa-IR")]
    [InlineData("ar-SA")]
    public void Display_text_is_the_same_in_every_culture(string culture) =>
        Gen.Select(Gen.Frequency((1, Rationals), (1, WideRationals)), Gen.OneOfConst(QuantityStyle.Fraction, QuantityStyle.Decimal))
            .Sample(iter: Iterations, assert: (value, style) =>
            {
                var before = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                    var invariant = QuantityText.Format(value, style);
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                    Assert.Equal(invariant, QuantityText.Format(value, style));
                }
                finally
                {
                    CultureInfo.CurrentCulture = before;
                }
            });

    // A test-only reader for exactly what QuantityText writes, so a round trip proves the text is exact. Reading what a
    // blogger writes is the parser's (PP4.5).
    private static Quantity Read(string text)
    {
        if (text == QuantityText.ToTaste)
        {
            return Quantity.ToTaste;
        }

        var ends = text.Split(QuantityText.RangeSeparator);
        return ends.Length == 2 ? Quantity.Range(ReadValue(ends[0]), ReadValue(ends[1])) : Quantity.Amount(ReadValue(text));
    }

    private static Rational ReadValue(string text)
    {
        var (numerator, denominator) = ReadExact(text);
        return Rational.Create((long)numerator, (long)denominator);
    }

    // The value in lowest terms, in BigInteger, so even long.MinValue's magnitude reads back.
    private static (BigInteger Numerator, BigInteger Denominator) ReadExact(string text)
    {
        if (text.StartsWith('-'))
        {
            var (n, d) = ReadExact(text[1..]);
            return (-n, d);
        }

        if (text.Contains('.', StringComparison.Ordinal))
        {
            var parts = text.Split('.');
            return Reduce(Whole(parts[0] + parts[1]), BigInteger.Pow(10, parts[1].Length));
        }

        if (text.Contains('/', StringComparison.Ordinal))
        {
            var space = text.IndexOf(' ', StringComparison.Ordinal);
            var whole = space < 0 ? BigInteger.Zero : Whole(text[..space]);
            var fraction = text[(space + 1)..].Split('/');
            var denominator = Whole(fraction[1]);
            return Reduce((whole * denominator) + Whole(fraction[0]), denominator);
        }

        if (Glyphs.TryGetValue(text[^1], out var glyph))
        {
            var whole = text.Length == 1 ? BigInteger.Zero : Whole(text[..^1]);
            return Reduce((whole * glyph.Denominator) + glyph.Numerator, glyph.Denominator);
        }

        return (Whole(text), BigInteger.One);
    }

    private static BigInteger Whole(string digits)
    {
        Assert.Matches(Digits(), digits);
        return BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private static (BigInteger Numerator, BigInteger Denominator) Reduce(BigInteger numerator, BigInteger denominator)
    {
        if (denominator < 0)
        {
            (numerator, denominator) = (-numerator, -denominator);
        }

        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return (numerator / divisor, denominator / divisor);
    }

    // The exact answer in lowest terms when it fits in 64 bits; otherwise the operation must throw OverflowException.
    private static void AssertExact(Func<Rational> operation, BigInteger numerator, BigInteger denominator)
    {
        var (n, d) = Reduce(numerator, denominator);
        if (n >= long.MinValue && n <= long.MaxValue && d <= long.MaxValue)
        {
            var result = operation();
            Assert.Equal((long)n, result.Numerator);
            Assert.Equal((long)d, result.Denominator);
        }
        else
        {
            Assert.Throws<OverflowException>(() => operation());
        }
    }

    private static Rational Remainder(Rational value)
    {
        var magnitude = value.Sign < 0 ? -value : value;
        return Rational.Create(magnitude.Numerator % magnitude.Denominator, magnitude.Denominator);
    }

    private static long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);

    [GeneratedRegex("^[0-9]+$")]
    private static partial Regex Digits();

    // A whole number; a kitchen fraction, after a whole number of one or more or alone; or n/d after a space or alone.
    [GeneratedRegex("^-?(0|[1-9][0-9]*|([1-9][0-9]*)?[½⅓⅔¼¾⅛⅜⅝⅞]|([1-9][0-9]* )?[1-9][0-9]*/[1-9][0-9]*)$")]
    private static partial Regex FractionShape();

    // A whole number, or one with one to three places and no trailing zero.
    [GeneratedRegex("^-?(0|[1-9][0-9]*)(\\.[0-9]{0,2}[1-9])?$")]
    private static partial Regex DecimalShape();
}
