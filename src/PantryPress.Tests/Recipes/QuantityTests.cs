using System.Text.RegularExpressions;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.2: an ingredient line's quantity (RCP-002, RCP-004): an amount, a range, or "to taste", always one a cook can
// measure, and never touched by floating point (PP-D05).
public sealed partial class QuantityTests
{
    [Fact]
    public void An_amount_holds_its_value_and_nothing_else()
    {
        var quantity = Quantity.Amount(Rational.Create(3, 2));

        Assert.Equal(QuantityKind.Amount, quantity.Kind);
        Assert.Equal(Rational.Create(3, 2), quantity.Low);
        Assert.Null(quantity.High);
        Assert.True(quantity.HasAmount);
    }

    [Fact]
    public void A_range_holds_both_ends_low_first()
    {
        var quantity = Quantity.Range(2, 3);

        Assert.Equal(QuantityKind.Range, quantity.Kind);
        Assert.Equal((Rational)2, quantity.Low);
        Assert.Equal((Rational)3, quantity.High);
        Assert.True(quantity.HasAmount);
    }

    [Fact]
    public void To_taste_has_no_amount_and_does_not_scale()
    {
        Assert.Equal(QuantityKind.ToTaste, Quantity.ToTaste.Kind);
        Assert.Null(Quantity.ToTaste.Low);
        Assert.Null(Quantity.ToTaste.High);
        Assert.False(Quantity.ToTaste.HasAmount);
        Assert.Same(Quantity.ToTaste, Quantity.ToTaste.MultiplyBy(3));
        Assert.Same(Quantity.ToTaste, Quantity.ToTaste.MultiplyBy(Rational.Create(1, 2)));
    }

    public static TheoryData<long, long> NotAboveZero => new() { { 0, 1 }, { -1, 1 }, { -1, 2 } };

    [Theory]
    [MemberData(nameof(NotAboveZero))]
    public void An_amount_or_a_range_end_is_above_zero(long n, long d)
    {
        var value = Rational.Create(n, d);

        Assert.Equal("value", Assert.Throws<ArgumentOutOfRangeException>(() => Quantity.Amount(value)).ParamName);
        Assert.Equal("low", Assert.Throws<ArgumentOutOfRangeException>(() => Quantity.Range(value, 1)).ParamName);
        Assert.Equal("factor", Assert.Throws<ArgumentOutOfRangeException>(() => Quantity.Amount(1).MultiplyBy(value)).ParamName);
        Assert.Equal("factor", Assert.Throws<ArgumentOutOfRangeException>(() => Quantity.Range(1, 2).MultiplyBy(value)).ParamName);
        Assert.Equal("factor", Assert.Throws<ArgumentOutOfRangeException>(() => Quantity.ToTaste.MultiplyBy(value)).ParamName);
    }

    [Theory]
    [InlineData(2, 1, 2, 1)]
    [InlineData(3, 1, 2, 1)]
    [InlineData(1, 2, 2, 4)]
    public void A_range_rises_from_its_low_end(long lowN, long lowD, long highN, long highD) =>
        Assert.Equal(
            "high",
            Assert.Throws<ArgumentOutOfRangeException>(() => Quantity.Range(Rational.Create(lowN, lowD), Rational.Create(highN, highD))).ParamName);

    [Fact]
    public void Equal_quantities_are_equal_however_their_values_were_written()
    {
        Assert.Equal(Quantity.Amount(Rational.Create(1, 2)), Quantity.Amount(Rational.Create(2, 4)));
        Assert.Equal(Quantity.Range(Rational.Create(1, 2), 1), Quantity.Range(Rational.Create(3, 6), Rational.Create(4, 4)));
        Assert.NotEqual(Quantity.Amount(2), Quantity.Range(2, 3));
        Assert.NotEqual(Quantity.Amount(2), Quantity.ToTaste);
    }

    [Fact]
    public void Multiplying_is_exact_for_an_amount_and_both_ends_of_a_range()
    {
        Assert.Equal(Quantity.Amount(Rational.Create(3, 4)), Quantity.Amount(Rational.Create(3, 2)).MultiplyBy(Rational.Create(1, 2)));
        Assert.Equal(Quantity.Amount(1), Quantity.Amount(Rational.Create(1, 3)).MultiplyBy(3));
        Assert.Equal(Quantity.Range(1, Rational.Create(3, 2)), Quantity.Range(2, 3).MultiplyBy(Rational.Create(1, 2)));
        Assert.Equal(Quantity.Range(6, 9), Quantity.Range(2, 3).MultiplyBy(3));
    }

    // RCP-004's named factors over a small corpus, exactly: rounding to friendly fractions is PP4.7's, and its scaling
    // tables are golden there. Each row is the quantity's fraction text at 1x, then at half, double, and triple.
    public static TheoryData<Quantity, string, string, string, string> NamedFactors => new()
    {
        { Quantity.Amount(1), "1", "½", "2", "3" },
        { Quantity.Amount(Rational.Create(3, 2)), "1½", "¾", "3", "4½" },
        { Quantity.Amount(Rational.Create(1, 3)), "⅓", "1/6", "⅔", "1" },
        { Quantity.Amount(Rational.Create(3, 4)), "¾", "⅜", "1½", "2¼" },
        { Quantity.Amount(Rational.Create(1, 8)), "⅛", "1/16", "¼", "⅜" },
        { Quantity.Amount(Rational.Create(5, 3)), "1⅔", "5/6", "3⅓", "5" },
        { Quantity.Amount(250), "250", "125", "500", "750" },
        { Quantity.Range(2, 3), "2–3", "1–1½", "4–6", "6–9" },
        { Quantity.Range(Rational.Create(1, 2), Rational.Create(3, 4)), "½–¾", "¼–⅜", "1–1½", "1½–2¼" },
        { Quantity.ToTaste, "to taste", "to taste", "to taste", "to taste" },
    };

    [Theory]
    [MemberData(nameof(NamedFactors))]
    public void Scaling_at_the_named_factors_is_exact(Quantity quantity, string once, string half, string twice, string thrice)
    {
        string At(Rational factor) => QuantityText.Format(quantity.MultiplyBy(factor), QuantityStyle.Fraction);

        Assert.Equal(once, At(1));
        Assert.Equal(half, At(Rational.Create(1, 2)));
        Assert.Equal(twice, At(2));
        Assert.Equal(thrice, At(3));
    }

    // The kinds and styles are stored (PP4.3) and sent, so their numbers never change.
    [Fact]
    public void The_stored_numbers_never_change()
    {
        Assert.Equal(0, (int)QuantityKind.Amount);
        Assert.Equal(1, (int)QuantityKind.Range);
        Assert.Equal(2, (int)QuantityKind.ToTaste);
        Assert.Equal(3, Enum.GetValues<QuantityKind>().Length);
        Assert.Equal(0, (int)QuantityStyle.Fraction);
        Assert.Equal(1, (int)QuantityStyle.Decimal);
        Assert.Equal(2, Enum.GetValues<QuantityStyle>().Length);
    }

    // PP-D05: recipe calculations never meet floating point. Every file of Recipes' Business is read, so scaling (PP4.7)
    // and conversion (PP4.8) are held to it as they arrive, with comments taken out so a doc comment may say "decimal".
    [Fact]
    public void Recipe_business_code_names_no_floating_point()
    {
        var business = new DirectoryInfo(Path.Combine(DomainSource.Modules.FullName, "Recipes", "Business"));
        var files = business.GetFiles("*.cs", SearchOption.AllDirectories);
        Assert.Contains(files, f => f.Name == "Rational.cs");
        Assert.Contains(files, f => f.Name == "QuantityText.cs");
        foreach (var file in files)
        {
            var code = WithoutComments(File.ReadAllText(file.FullName));
            Assert.True(FloatingPoint().Matches(code).Count == 0, $"{file.Name} names floating point: {FloatingPoint().Match(code).Value}");
        }
    }

    [Theory]
    [InlineData("double x = 0;")]
    [InlineData("float x = 0;")]
    [InlineData("decimal x = 0;")]
    [InlineData("System.Double x;")]
    [InlineData("System.Decimal x;")]
    [InlineData("var x = Decimal.Parse(s);")]
    [InlineData("var h = (Half)1;")]
    [InlineData("var r = Math.Round(x);")]
    [InlineData("var r = MathF.Sqrt(x);")]
    [InlineData("var d = Convert.ToDouble(n);")]
    [InlineData("var d = Convert.ToDecimal(n);")]
    [InlineData("var x = 2.5;")]
    [InlineData("var x = 1.5d;")]
    [InlineData("var x = 3m;")]
    [InlineData("var x = 1e3;")]
    [InlineData("NFloat x;")]
    [InlineData("var d = BitConverter.Int64BitsToDouble(n);")]
    [InlineData("var f = BitConverter.Int32BitsToSingle(n);")]
    public void The_floating_point_scan_finds_floating_point(string code) =>
        Assert.Matches(FloatingPoint(), WithoutComments(code));

    [Theory]
    [InlineData("QuantityStyle.Decimal => IsExactDecimal(value) ? FormatDecimal(value) : FormatFraction(value),")]
    [InlineData("private const long DecimalScale = 1_000;")]
    [InlineData("var places = scaled % DecimalScale;")]
    [InlineData("var x = 0x1F;")]
    [InlineData("// a decimal that ends within three places, never a double")]
    [InlineData("var x = 1; // 0.5 as a double")]
    public void The_floating_point_scan_lets_exact_code_by(string code) =>
        Assert.DoesNotMatch(FloatingPoint(), WithoutComments(code));

    private static string WithoutComments(string code) => LineComment().Replace(code, string.Empty);

    // No scanned file has "//" inside a string, so a line comment runs from "//" to the end of the line.
    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment();

    // Floating-point keywords and types, Math, conversions to them, and literals with a point, an exponent, or a suffix.
    // Decimal alone is QuantityStyle's member name, so that type is caught by its full name or a member access.
    [GeneratedRegex(@"\b(double|float|decimal|Double|Single|Half|NFloat|MathF?|ToDouble|ToSingle|ToDecimal|ToHalf)\b|BitsTo(Double|Single|Half)\b|\bSystem\.Decimal\b|\bDecimal\s*\.|\b\d[\d_]*(\.\d[\d_]*([eE][+-]?\d+)?[dDfFmM]?|[eE][+-]?\d+[dDfFmM]?|[dDfFmM])\b")]
    private static partial Regex FloatingPoint();
}
