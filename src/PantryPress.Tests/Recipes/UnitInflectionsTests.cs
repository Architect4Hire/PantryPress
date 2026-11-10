using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.7: the unit words a scaled line may put in the singular or plural (RCP-004), kept on the vocabulary (PP4.1): every
// listed word is a spelling of one unit in both numbers, no abbreviation or temperature is listed, and every unit's own
// words are.
public sealed class UnitInflectionsTests
{
    private static readonly ILookup<string, string> UnitsBySpelling =
        Units.Aliases.Where(a => !a.CaseSensitive).ToLookup(a => a.Alias, a => a.UnitCode, StringComparer.Ordinal);

    [Fact]
    public void Both_numbers_of_every_word_spell_the_same_unit()
    {
        foreach (var word in UnitInflections.All)
        {
            var singular = Assert.Single(UnitsBySpelling[word.Singular]);
            var plural = Assert.Single(UnitsBySpelling[word.Plural]);
            Assert.Equal(singular, plural);
        }
    }

    [Fact]
    public void Each_word_is_listed_once()
    {
        var spellings = UnitInflections.All.SelectMany(w => new[] { w.Singular, w.Plural }).ToList();
        Assert.Equal(spellings.Count, spellings.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void No_abbreviation_or_temperature_is_listed()
    {
        var abbreviations = Units.All.Select(u => u.Abbreviation).OfType<string>().Select(a => a.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var temperatures = Units.All.Where(u => u.Dimension == UnitDimension.Temperature).Select(u => u.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var word in UnitInflections.All)
        {
            Assert.DoesNotContain(word.Singular, abbreviations);
            Assert.DoesNotContain(word.Plural, abbreviations);
            Assert.DoesNotContain(UnitsBySpelling[word.Singular].Single(), temperatures);
        }
    }

    [Fact]
    public void Every_unit_that_scales_has_its_own_words_listed()
    {
        foreach (var unit in Units.All.Where(u => u.Dimension != UnitDimension.Temperature))
        {
            Assert.Contains(UnitInflections.All, w => w.Singular == unit.Singular && w.Plural == unit.Plural);
        }
    }

    [Theory]
    [InlineData("cup", true, "cups")]
    [InlineData("cups", true, "cups")]
    [InlineData("cups", false, "cup")]
    [InlineData("cup", false, "cup")]
    [InlineData("Cup", true, "Cups")]
    [InlineData("CUP", true, "CUPS")]
    [InlineData("CUPS", false, "CUP")]
    [InlineData("pinch", true, "pinches")]
    [InlineData("PINCH", true, "PINCHES")]
    [InlineData("pinches", false, "pinch")]
    [InlineData("PINCHES", false, "PINCH")]
    [InlineData("FLUID OUNCE", true, "FLUID OUNCES")]
    [InlineData("cuP", true, "cuPS")]
    [InlineData("cUp", true, "cUps")]
    [InlineData("Tablespoon", true, "Tablespoons")]
    [InlineData("teaspoonful", true, "teaspoonfuls")]
    [InlineData("fluid ounce", true, "fluid ounces")]
    [InlineData("fluid  ounce", true, "fluid  ounces")]
    [InlineData("fluid\tounces", false, "fluid\tounce")]
    [InlineData("litre", true, "litres")]
    [InlineData("kilos", false, "kilo")]

    // Abbreviations and anything unlisted are never changed.
    [InlineData("tbsp", true, "tbsp")]
    [InlineData("lbs", false, "lbs")]
    [InlineData("g", true, "g")]
    [InlineData("c", true, "c")]
    [InlineData("T", true, "T")]
    [InlineData("fl oz", true, "fl oz")]
    [InlineData("°F", true, "°F")]
    [InlineData("tin", true, "tin")]
    [InlineData("", true, "")]
    public void A_listed_word_agrees_in_number_and_keeps_the_bloggers_case(string written, bool plural, string expected) =>
        Assert.Equal(expected, UnitInflections.Agree(written, plural));

    // Only ASCII letters fold, as UnitSpellings matches: the Kelvin sign is no k.
    [Fact]
    public void A_look_alike_letter_is_not_a_listed_word() =>
        Assert.Equal("Kilo", UnitInflections.Agree("Kilo", plural: true));
}
