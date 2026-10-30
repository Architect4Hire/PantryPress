using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.3: a quantity as its columns hold it. Written from a Quantity and read back through Rational.Create and Quantity's own
// factories, exactly, so a stored value never skips their rules; columns that make no quantity are a bug and throw.
public sealed class StoredQuantityTests
{
    public static TheoryData<Quantity?> Quantities => new()
    {
        null,
        Quantity.ToTaste,
        Quantity.Amount(1),
        Quantity.Amount(Rational.Create(1, 3)),
        Quantity.Amount(Rational.Create(7, 4)),
        Quantity.Range(2, 3),
        Quantity.Range(Rational.Create(1, 2), Rational.Create(3, 4)),
        Quantity.Amount(RecipeLimits.MaxQuantityTerm),
        Quantity.Amount(Rational.Create(1, RecipeLimits.MaxQuantityTerm)),
        Quantity.Range(Rational.Create(RecipeLimits.MaxQuantityTerm - 1, RecipeLimits.MaxQuantityTerm), 1),
    };

    [Theory]
    [MemberData(nameof(Quantities))]
    public void A_quantity_comes_back_exactly(Quantity? quantity)
    {
        var stored = StoredQuantity.From(quantity);

        Assert.Equal(quantity, stored.ToQuantity());
        Assert.Equal(quantity?.Kind, stored.Kind);
    }

    [Fact]
    public void Each_kind_fills_only_its_own_columns()
    {
        Assert.Equal(default, StoredQuantity.From(null));
        Assert.Equal(new StoredQuantity(QuantityKind.ToTaste, null, null, null, null), StoredQuantity.From(Quantity.ToTaste));
        Assert.Equal(
            new StoredQuantity(QuantityKind.Amount, 3, 2, null, null),
            StoredQuantity.From(Quantity.Amount(Rational.Create(6, 4))));
        Assert.Equal(
            new StoredQuantity(QuantityKind.Range, 1, 2, 1, 1),
            StoredQuantity.From(Quantity.Range(Rational.Create(2, 4), 1)));
    }

    // Raw SQL can write a fraction that is not in lowest terms; reading it back reduces it.
    [Fact]
    public void A_stored_fraction_is_read_in_lowest_terms()
    {
        var read = new StoredQuantity(QuantityKind.Amount, 4, 8, null, null).ToQuantity()!;

        Assert.Equal(Rational.Create(1, 2), read.Low);
        Assert.Equal(1, read.Low!.Value.Numerator);
    }

    [Theory]
    [InlineData(RecipeLimits.MaxQuantityTerm + 1, 1)]
    [InlineData(1, RecipeLimits.MaxQuantityTerm + 1)]
    [InlineData(long.MaxValue, 1)]
    public void A_term_past_the_cap_is_refused_before_the_table_would(long numerator, long denominator)
    {
        var amount = Quantity.Amount(Rational.Create(numerator, denominator));

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => StoredQuantity.From(amount));
        Assert.Equal("quantity", error.ParamName);
    }

    [Fact]
    public void Either_end_of_a_range_past_the_cap_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StoredQuantity.From(Quantity.Range(1, RecipeLimits.MaxQuantityTerm + 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StoredQuantity.From(Quantity.Range(Rational.Create(1, RecipeLimits.MaxQuantityTerm + 1), 1)));
    }

    public static TheoryData<StoredQuantity> Malformed => new()
    {
        new(null, 1, 1, null, null),
        new(null, null, null, 2, 1),
        new(QuantityKind.ToTaste, 1, 1, null, null),
        new(QuantityKind.Amount, null, null, null, null),
        new(QuantityKind.Amount, 1, null, null, null),
        new(QuantityKind.Amount, 1, 1, 2, 1),
        new(QuantityKind.Range, 1, 1, null, null),
        new(QuantityKind.Range, 1, 1, 2, null),
        new((QuantityKind)7, 1, 1, null, null),
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void Columns_that_make_no_quantity_throw(StoredQuantity stored) =>
        Assert.Throws<InvalidOperationException>(() => stored.ToQuantity());

    // Columns that are shaped right but hold a value Quantity refuses are refused by Quantity, as from anywhere else.
    [Theory]
    [InlineData(0L, 1L, null, null)]
    [InlineData(-1L, 2L, null, null)]
    [InlineData(1L, 1L, 1L, 1L)]
    [InlineData(3L, 1L, 2L, 1L)]
    public void Columns_holding_a_value_quantity_refuses_throw(long lowN, long lowD, long? highN, long? highD)
    {
        var stored = new StoredQuantity(highN is null ? QuantityKind.Amount : QuantityKind.Range, lowN, lowD, highN, highD);

        Assert.Throws<ArgumentOutOfRangeException>(() => stored.ToQuantity());
    }

    [Fact]
    public void A_zero_denominator_throws() =>
        Assert.Throws<DivideByZeroException>(() => new StoredQuantity(QuantityKind.Amount, 1, 0, null, null).ToQuantity());

    [Theory]
    [MemberData(nameof(Quantities))]
    public void An_ingredient_line_holds_its_quantity_through_its_columns(Quantity? quantity)
    {
        var line = new RecipeIngredient { Quantity = quantity };

        Assert.Equal(quantity, line.Quantity);
    }

    // A yield counts something - "Serves 4", "Makes 24–30" - so it is never "to taste".
    [Fact]
    public void A_yield_is_an_amount_or_a_range()
    {
        var recipe = new Recipe { Yield = Quantity.Range(4, 6) };
        Assert.Equal(Quantity.Range(4, 6), recipe.Yield);

        recipe.Yield = null;
        Assert.Null(recipe.Yield);

        var error = Assert.Throws<ArgumentException>(() => recipe.Yield = Quantity.ToTaste);
        Assert.Equal("value", error.ParamName);
        Assert.Null(recipe.Yield);
    }
}
