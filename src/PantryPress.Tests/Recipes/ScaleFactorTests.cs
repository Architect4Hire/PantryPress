using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.7: the factors a reader scales a recipe by (RCP-004): the four fixed choices and custom servings, exact.
public sealed class ScaleFactorTests
{
    [Fact]
    public void The_fixed_choices_are_half_one_two_and_three_in_that_order()
    {
        Assert.Equal(
            [Rational.Create(1, 2), Rational.One, 2, 3],
            ScaleFactor.Multipliers.Select(f => f.Value).ToArray());
        Assert.Same(ScaleFactor.Halved, ScaleFactor.Multipliers[0]);
        Assert.Same(ScaleFactor.Unchanged, ScaleFactor.Multipliers[1]);
        Assert.Same(ScaleFactor.Doubled, ScaleFactor.Multipliers[2]);
        Assert.Same(ScaleFactor.Tripled, ScaleFactor.Multipliers[3]);
        Assert.True(ScaleFactor.Unchanged.IsUnchanged);
        Assert.Single(ScaleFactor.Multipliers, f => f.IsUnchanged);
    }

    [Fact]
    public void A_factor_is_exact_and_equal_by_value()
    {
        Assert.Equal(ScaleFactor.Of(Rational.Create(6, 8)), ScaleFactor.Of(Rational.Create(3, 4)));
        Assert.Equal(Rational.Create(3, 4), ScaleFactor.Of(Rational.Create(3, 4)).Value);
        Assert.Equal(ScaleFactor.Doubled, ScaleFactor.Of(2));
        Assert.Equal("3/4x", ScaleFactor.Of(Rational.Create(3, 4)).ToString());
    }

    public static TheoryData<long, long> Refused => new()
    {
        { 0, 1 },
        { -1, 2 },
        { RecipeLimits.MaxQuantityTerm + 1, 1 },
        { 1, RecipeLimits.MaxQuantityTerm + 1 },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void A_factor_is_above_zero_with_terms_at_most_the_stored_cap(long n, long d) =>
        Assert.Equal("value", Assert.Throws<ArgumentOutOfRangeException>(() => ScaleFactor.Of(Rational.Create(n, d))).ParamName);

    [Fact]
    public void The_cap_itself_is_a_factor()
    {
        Assert.Equal(RecipeLimits.MaxQuantityTerm, ScaleFactor.Of(RecipeLimits.MaxQuantityTerm).Value.Numerator);
        Assert.Equal(RecipeLimits.MaxQuantityTerm, ScaleFactor.Of(Rational.Create(1, RecipeLimits.MaxQuantityTerm)).Value.Denominator);
    }

    [Theory]
    [InlineData(4, 3, 3, 4)]
    [InlineData(4, 5, 5, 4)]
    [InlineData(4, 8, 2, 1)]
    [InlineData(4, 4, 1, 1)]
    [InlineData(12, 1, 1, 12)]
    [InlineData(1, 100, 100, 1)]
    public void Custom_servings_are_the_servings_over_a_whole_yield(long yield, long servings, long n, long d) =>
        Assert.Equal(Rational.Create(n, d), ScaleFactor.ForServings(Quantity.Amount(yield), servings)!.Value);

    [Fact]
    public void Custom_servings_at_the_edges()
    {
        Assert.True(ScaleFactor.ForServings(Quantity.Amount(4), 4)!.IsUnchanged);
        Assert.Equal(Rational.Create(1, RecipeLimits.MaxQuantityTerm), ScaleFactor.ForServings(Quantity.Amount(RecipeLimits.MaxQuantityTerm), 1)!.Value);
        Assert.Equal((Rational)RecipeLimits.MaxQuantityTerm, ScaleFactor.ForServings(Quantity.Amount(1), RecipeLimits.MaxQuantityTerm)!.Value);
    }

    public static TheoryData<Quantity?> NoCustomServings => new()
    {
        null,
        Quantity.ToTaste,
        Quantity.Range(4, 6),
        Quantity.Amount(Rational.Create(3, 2)),
        Quantity.Amount(RecipeLimits.MaxQuantityTerm + 1),
    };

    [Theory]
    [MemberData(nameof(NoCustomServings))]
    public void A_yield_that_is_not_one_whole_number_has_no_custom_servings(Quantity? yield) =>
        Assert.Null(ScaleFactor.ForServings(yield, 4));

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(RecipeLimits.MaxQuantityTerm + 1)]
    public void Servings_are_a_whole_number_from_one_to_the_cap(long servings) =>
        Assert.Equal("servings", Assert.Throws<ArgumentOutOfRangeException>(() => ScaleFactor.ForServings(Quantity.Amount(4), servings)).ParamName);
}
