using System.Globalization;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.5: the parser's rules that the corpus cannot show line by line - the length limits, which need lines built to size,
// and the unit spellings, checked against the whole vocabulary - and the ones worth naming on their own.
public sealed class IngredientLineParserTests
{
    [Theory]
    [InlineData("one", 1)]
    [InlineData("two", 2)]
    [InlineData("three", 3)]
    [InlineData("four", 4)]
    [InlineData("five", 5)]
    [InlineData("six", 6)]
    [InlineData("seven", 7)]
    [InlineData("eight", 8)]
    [InlineData("nine", 9)]
    [InlineData("ten", 10)]
    [InlineData("eleven", 11)]
    [InlineData("twelve", 12)]
    public void Every_number_word_has_its_value(string word, int value)
    {
        foreach (var written in new[] { word, word.ToUpperInvariant(), char.ToUpperInvariant(word[0]) + word[1..] })
        {
            var parse = IngredientLineParser.Parse(written + " eggs");
            Assert.Equal(Quantity.Amount(value), parse.Quantity);
            Assert.Equal(new TextSpan(0, word.Length), parse.QuantitySpan);
            Assert.Equal("eggs", parse.Item);
        }
    }

    // Nothing reads the culture: the corpus parses the same under Turkish (whose capital I lower-cases to a dotless one),
    // Arabic, and the invariant culture as under the machine's own.
    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("")]
    public void The_corpus_parses_the_same_under_any_culture(string name)
    {
        var lines = IngredientCorpus.Load().Select(c => c.Input).ToList();
        var expected = lines.Select(l => IngredientCorpus.Describe(IngredientLineParser.Parse(l))).ToList();
        var (culture, ui) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
            Assert.Equal(expected, lines.Select(l => IngredientCorpus.Describe(IngredientLineParser.Parse(l))));
            Assert.Equal(Quantity.Amount(6), IngredientLineParser.Parse("SIX EGGS").Quantity);
            Assert.Equal("pinch", IngredientLineParser.Parse("1 PINCH SALT").UnitCode);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (culture, ui);
        }
    }

    // The style a scaled amount is written back in (PP4.7): a decimal point makes it decimal, anything else a fraction.
    [Theory]
    [InlineData("1.5 cups flour", QuantityStyle.Decimal)]
    [InlineData(".5 cup milk", QuantityStyle.Decimal)]
    [InlineData("1.5-2 kg beef", QuantityStyle.Decimal)]
    [InlineData("1/2-0.75 cup water", QuantityStyle.Decimal)]
    [InlineData("2 cups flour", QuantityStyle.Fraction)]
    [InlineData("1 1/2 cups flour", QuantityStyle.Fraction)]
    [InlineData("1½ cups flour", QuantityStyle.Fraction)]
    [InlineData("2-3 cloves garlic", QuantityStyle.Fraction)]
    [InlineData("two eggs", QuantityStyle.Fraction)]
    [InlineData("a pinch of salt", QuantityStyle.Fraction)]
    [InlineData("1 (8.5 oz) can corn", QuantityStyle.Fraction)]
    [InlineData("salt, to taste", null)]
    [InlineData("salt", null)]
    [InlineData("1 cup + 2 tbsp flour", null)]
    public void The_style_is_how_the_amount_was_written(string line, QuantityStyle? style) =>
        Assert.Equal(style, IngredientLineParser.Parse(line).Style);

    // The parser reads by hand-written rules over tokens; a regular expression could backtrack on a pasted line.
    [Fact]
    public void Recipe_business_code_names_no_regular_expression()
    {
        var business = new DirectoryInfo(Path.Combine(DomainSource.Modules.FullName, "Recipes", "Business"));
        var files = business.GetFiles("*.cs", SearchOption.AllDirectories);
        Assert.Contains(files, f => f.Name == "IngredientLineParser.cs");
        foreach (var file in files)
        {
            var code = File.ReadAllText(file.FullName);
            Assert.DoesNotContain("RegularExpressions", code, StringComparison.Ordinal);
            Assert.DoesNotContain("Regex", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_line_is_required() =>
        Assert.Throws<ArgumentNullException>(() => IngredientLineParser.Parse(null!));

    [Fact]
    public void Broken_text_is_flagged_before_anything_else()
    {
        Assert.Equal(IngredientParseProblem.IllFormedText, IngredientLineParser.Parse("(\u0007").Problem);
        Assert.Equal(IngredientParseProblem.TooLong, IngredientLineParser.Parse("\uD83C" + new string('x', RecipeLimits.MaxIngredientTextLength)).Problem);
    }

    [Fact]
    public void A_line_up_to_the_stored_length_is_read_and_a_longer_one_is_flagged()
    {
        var longest = "2 cups flour, " + new string('x', RecipeLimits.MaxIngredientTextLength - 14);
        Assert.Equal(RecipeLimits.MaxIngredientTextLength, longest.Length);
        Assert.Equal(IngredientParseProblem.NoteTooLong, IngredientLineParser.Parse(longest).Problem);

        var fits = "2 cups flour" + new string(' ', RecipeLimits.MaxIngredientTextLength - 12);
        Assert.Equal(IngredientParseStatus.Parsed, IngredientLineParser.Parse(fits).Status);
        Assert.Equal(IngredientParseProblem.TooLong, IngredientLineParser.Parse(fits + " ").Problem);
    }

    [Fact]
    public void An_item_up_to_its_stored_length_is_read_and_a_longer_one_is_flagged()
    {
        var item = new string('x', RecipeLimits.MaxIngredientItemLength);
        Assert.Equal(item, IngredientLineParser.Parse("1 cup " + item).Item);
        Assert.Equal(IngredientParseProblem.ItemTooLong, IngredientLineParser.Parse("1 cup " + item + "x").Problem);
    }

    // The limit is on the note as stored, its pieces and their separators together.
    [Fact]
    public void A_note_up_to_its_stored_length_is_read_and_a_longer_one_is_flagged()
    {
        var first = new string('a', 100);
        var second = new string('b', RecipeLimits.MaxIngredientNoteLength - 100 - IngredientLineParse.NoteSeparator.Length);
        var parse = IngredientLineParser.Parse($"1 cup flour ({first}), {second}");
        Assert.Equal(first + IngredientLineParse.NoteSeparator + second, parse.Note);
        Assert.Equal(RecipeLimits.MaxIngredientNoteLength, parse.Note!.Length);
        Assert.Equal(IngredientParseProblem.NoteTooLong, IngredientLineParser.Parse($"1 cup flour ({first}), {second}b").Problem);
    }

    // Every spelling in the vocabulary reads as its own unit, over its whole length, in the case it is listed in and - for
    // a case-insensitive one - in capitals, whatever order the list is in.
    [Fact]
    public void Every_spelling_reads_as_its_unit()
    {
        foreach (var alias in Units.Aliases)
        {
            Assert.Equal(new UnitMatch(alias.UnitCode, alias.Alias.Length), UnitSpellings.Match(alias.Alias + " flour", 0));
            if (!alias.CaseSensitive)
            {
                Assert.Equal(new UnitMatch(alias.UnitCode, alias.Alias.Length), UnitSpellings.Match(alias.Alias.ToUpperInvariant() + " flour", 0));
            }

            var parse = IngredientLineParser.Parse("2 " + alias.Alias + " flour");
            Assert.Equal(alias.UnitCode, parse.UnitCode);
            Assert.Equal(new TextSpan(2, alias.Alias.Length), parse.UnitSpan);
        }
    }

    [Theory]
    [InlineData("2 lbs flour", "pound", 3)]
    [InlineData("2 l flour", "liter", 1)]
    [InlineData("2 tbsp. flour", "tablespoon", 5)]
    [InlineData("2 fl. oz. rum", "fluid-ounce", 7)]
    [InlineData("2 fl  oz rum", "fluid-ounce", 6)]
    [InlineData("2 fl\u00A0oz rum", "fluid-ounce", 5)]
    [InlineData("2 degrees   fahrenheit water", "fahrenheit", 20)]
    [InlineData("2 T flour", "tablespoon", 1)]
    [InlineData("2 t flour", "teaspoon", 1)]
    [InlineData("2 C flour", "cup", 1)]
    [InlineData("2 c; flour", "cup", 1)]
    [InlineData("2 cups(sifted) flour", "cup", 4)]
    public void The_longest_spelling_is_read(string line, string unit, int length)
    {
        var parse = IngredientLineParser.Parse(line);
        Assert.Equal(unit, parse.UnitCode);
        Assert.Equal(new TextSpan(2, length), parse.UnitSpan);
        Assert.NotNull(parse.Item);
    }

    // A unit ends where a word does, and only ASCII letters fold, so no look-alike becomes a unit or a number word.
    [Theory]
    [InlineData("1 lemon")]
    [InlineData("1 T-bone steak")]
    [InlineData("1 cup's worth of rice")]
    [InlineData("1 cups½ flour")]
    [InlineData("1 c\u0327ups flour")]
    [InlineData("1 \u212Ag flour")]
    [InlineData("1 \u017Fprig thyme")]
    [InlineData("1 TBSP\u0130 sugar")]
    public void A_look_alike_or_a_longer_word_is_no_unit(string line) =>
        Assert.Null(IngredientLineParser.Parse(line).UnitCode);

    [Theory]
    [InlineData("\u017Fix eggs")]
    [InlineData("tw\u03BF eggs")]
    public void A_look_alike_is_no_number_word(string line) =>
        Assert.Null(IngredientLineParser.Parse(line).Quantity);

    [Fact]
    public void The_same_line_parses_the_same_every_time()
    {
        foreach (var c in IngredientCorpus.Load())
        {
            Assert.Equal(IngredientLineParser.Parse(c.Input), IngredientLineParser.Parse(c.Input));
        }
    }

    // The line is never rewritten: every part is cut from it, and the spans index it as written, so scaling (PP4.7) can
    // replace the quantity's span and leave every other character where it was.
    [Fact]
    public void Spans_index_the_line_as_written()
    {
        const string line = "▢ 1½\u00A0cups (250 g) all-purpose flour, sifted";
        var parse = IngredientLineParser.Parse(line);
        Assert.Equal("1½", line.Substring(parse.QuantitySpan!.Value.Start, parse.QuantitySpan.Value.Length));
        Assert.Equal("cups", line.Substring(parse.UnitSpan!.Value.Start, parse.UnitSpan.Value.Length));
        Assert.Equal("all-purpose flour", parse.Item);
        Assert.Equal("250 g; sifted", parse.Note);

        var scaled = string.Concat(line.AsSpan(0, parse.QuantitySpan.Value.Start), "3", line.AsSpan(parse.QuantitySpan.Value.End));
        Assert.Equal("▢ 3\u00A0cups (250 g) all-purpose flour, sifted", scaled);
    }

    [Fact]
    public void A_range_end_may_carry_its_unit_without_a_space()
    {
        var parse = IngredientLineParser.Parse("2-3cups flour");
        Assert.Equal(Quantity.Range(2, 3), parse.Quantity);
        Assert.Equal("cup", parse.UnitCode);
        Assert.Equal(new TextSpan(3, 4), parse.UnitSpan);
    }

    [Fact]
    public void An_article_counts_only_before_a_unit()
    {
        var withSize = IngredientLineParser.Parse("a 14-ounce can tomatoes");
        Assert.Equal(Quantity.Amount(1), withSize.Quantity);
        Assert.Equal(new TextSpan(0, 1), withSize.QuantitySpan);
        Assert.Equal(("can", "tomatoes", "14-ounce"), (withSize.UnitCode!, withSize.Item!, withSize.Note!));

        var noUnit = IngredientLineParser.Parse("a 9-inch pie crust");
        Assert.Null(noUnit.Quantity);
        Assert.Equal("a 9-inch pie crust", noUnit.Item);

        // Joined to the unit, "a" is a word of the item.
        Assert.Null(IngredientLineParser.Parse("acup flour").Quantity);
    }

    [Fact]
    public void An_unparsed_line_keeps_no_part()
    {
        var parse = IngredientLineParser.Parse("1 cup + 2 tbsp flour");
        Assert.Equal(IngredientParseStatus.Unparsed, parse.Status);
        Assert.Equal(IngredientParseProblem.AmbiguousQuantity, parse.Problem);
        IngredientParseInvariants.Check("1 cup + 2 tbsp flour", parse);
    }

    // PP4.7: the scaler reads its own output again, which may be longer than a stored line. Within the cap the uncapped
    // reading is the parser's, line for line; past it, it reads rather than refusing.
    [Fact]
    public void Reading_past_the_length_cap_agrees_with_the_parser_within_it()
    {
        foreach (var input in IngredientCorpus.Load().Select(c => c.Input).Where(t => t.Length <= RecipeLimits.MaxIngredientTextLength))
        {
            Assert.Equal(IngredientLineParser.Parse(input), IngredientLineParser.ParseAnyLength(input));
        }

        var longLine = "2 cups flour" + new string(' ', RecipeLimits.MaxIngredientTextLength);
        Assert.Equal(IngredientParseProblem.TooLong, IngredientLineParser.Parse(longLine).Problem);
        var read = IngredientLineParser.ParseAnyLength(longLine);
        Assert.Equal(IngredientParseStatus.Parsed, read.Status);
        Assert.Equal(Quantity.Amount(2), read.Quantity);
        Assert.Equal("cup", read.UnitCode);
        Assert.Throws<ArgumentNullException>(() => IngredientLineParser.ParseAnyLength(null!));
    }
}
