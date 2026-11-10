using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.8: the conversion factors and the density table, the platform reference data conversion reads (RCP-005, PP-D06).
public sealed class ConversionReferenceTests
{
    // Every factor is its unit's legal definition, stated here on its own: a US gallon is 3.785411784 L, a pound
    // 453.59237 g.
    [Theory]
    [InlineData("gallon", 3_785_411_784, 1_000_000)]
    [InlineData("quart", 3_785_411_784, 4_000_000)]
    [InlineData("pint", 3_785_411_784, 8_000_000)]
    [InlineData("cup", 3_785_411_784, 16_000_000)]
    [InlineData("fluid-ounce", 3_785_411_784, 128_000_000)]
    [InlineData("tablespoon", 3_785_411_784, 256_000_000)]
    [InlineData("teaspoon", 3_785_411_784, 768_000_000)]
    [InlineData("milliliter", 1, 1)]
    [InlineData("deciliter", 100, 1)]
    [InlineData("liter", 1_000, 1)]
    [InlineData("pound", 45_359_237, 100_000)]
    [InlineData("ounce", 45_359_237, 1_600_000)]
    [InlineData("gram", 1, 1)]
    [InlineData("kilogram", 1_000, 1)]
    public void Every_factor_is_its_units_definition(string code, long numerator, long denominator) =>
        Assert.Equal(Rational.Create(numerator, denominator), UnitConversions.InBase(code));

    // Exactly the US and metric volume and weight units convert by a factor; counts and temperatures have none.
    [Fact]
    public void Every_convertible_unit_has_a_factor_and_nothing_else_does()
    {
        foreach (var unit in Units.All)
        {
            var converts = unit.Dimension is UnitDimension.Volume or UnitDimension.Weight && unit.System != UnitSystem.Neutral;
            Assert.Equal(converts, UnitConversions.Converts(unit));
            Assert.Equal(converts, UnitConversions.InBase(unit.Code) is not null);
        }

        Assert.Equal(UnitConversions.All.Count, UnitConversions.All.Select(e => e.UnitCode).Distinct(StringComparer.Ordinal).Count());
        Assert.All(UnitConversions.All, e => Assert.Contains(Units.All, u => u.Code == e.UnitCode));
        Assert.All(UnitConversions.All, e => Assert.True(e.InBase.Sign > 0));
        Assert.Equal(UnitConversions.Version, UnitConversions.All.Max(e => e.Since));
    }

    // The densities are King Arthur Baking's chart, per US cup, read 2026-11-10; butter is the exact half pound.
    [Theory]
    [InlineData("all-purpose-flour", 120, 1)]
    [InlineData("bread-flour", 120, 1)]
    [InlineData("cake-flour", 120, 1)]
    [InlineData("whole-wheat-flour", 113, 1)]
    [InlineData("granulated-sugar", 198, 1)]
    [InlineData("brown-sugar", 213, 1)]
    [InlineData("confectioners-sugar", 113, 1)]
    [InlineData("butter", 45_359_237, 200_000)]
    [InlineData("cocoa", 84, 1)]
    [InlineData("rolled-oats", 89, 1)]
    [InlineData("honey", 336, 1)]
    [InlineData("chocolate-chips", 170, 1)]
    public void Every_density_is_pinned(string code, long numerator, long denominator) =>
        Assert.Equal(Rational.Create(numerator, denominator), IngredientDensities.All.Single(e => e.Code == code).GramsPerCup);

    [Fact]
    public void The_density_table_is_well_formed()
    {
        Assert.Equal(12, IngredientDensities.All.Count);
        Assert.Equal(IngredientDensities.Version, IngredientDensities.All.Max(e => e.Since));
        Assert.Equal(IngredientDensities.All.Count, IngredientDensities.All.Select(e => e.Code).Distinct(StringComparer.Ordinal).Count());
        var spellings = IngredientDensities.All.SelectMany(e => e.Spellings).ToList();
        Assert.Equal(spellings.Count, spellings.Distinct(StringComparer.Ordinal).Count());
        foreach (var entry in IngredientDensities.All)
        {
            Assert.Matches("^[a-z]+(-[a-z]+)*$", entry.Code);
            Assert.True(entry.GramsPerCup.Sign > 0);
            Assert.NotEmpty(entry.Spellings);
            foreach (var spelling in entry.Spellings)
            {
                // Stored folded, ASCII only, and never a unit's spelling, so a unit can never be read as an item.
                Assert.Equal(spelling, IngredientDensities.Fold(spelling));
                Assert.True(spelling.All(char.IsAscii), spelling);
                Assert.DoesNotContain(Units.Aliases, a => string.Equals(a.Alias, spelling, StringComparison.OrdinalIgnoreCase));
                Assert.Same(entry, IngredientDensities.Find(spelling));
            }
        }
    }

    // Only ASCII case, whitespace, and the curly apostrophe fold; anything else must be exactly a spelling.
    [Theory]
    [InlineData("All-Purpose Flour", "all-purpose-flour")]
    [InlineData("  all-purpose \t flour ", "all-purpose-flour")]
    [InlineData("all-purpose\u00A0flour", "all-purpose-flour")]
    [InlineData("all-purpose\u200Bflour", "all-purpose-flour")]
    [InlineData("all-purpose \u200B flour", "all-purpose-flour")]
    [InlineData("confectioners\u2019 sugar", "confectioners-sugar")]
    [InlineData("confectioners\u02BC sugar", null)]
    [InlineData("flour, sifted", null)]
    [InlineData("sifted flour", null)]
    [InlineData("flours", null)]
    [InlineData("\uFF46lour", null)]
    [InlineData("FLOUR", "all-purpose-flour")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void An_item_matches_only_a_folded_spelling(string? item, string? code) =>
        Assert.Equal(code, IngredientDensities.Find(item)?.Code);
}
