using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.7: the scaler's rules one at a time (RCP-004). The golden table (ScalingCorpusTests) shows them over real lines; these
// pin each rule, its edges, and what the scaler refuses.
public sealed class RecipeScalerTests
{
    private static readonly ScaleFactor ThreeQuarters = ScaleFactor.Of(Rational.Create(3, 4));

    [Theory]
    [InlineData("2 cups flour")]
    [InlineData("1 cup + 2 tbsp flour")]
    [InlineData("Juice of 2 lemons")]
    [InlineData("salt")]
    [InlineData("350°F oven")]
    public void At_one_every_line_is_exactly_as_written_and_unflagged(string text)
    {
        var parse = IngredientLineParser.Parse(text);
        var scaled = Scale(text, ScaleFactor.Unchanged);

        Assert.Equal(text, scaled.Text);
        Assert.Equal(parse.Quantity, scaled.Quantity);
        Assert.Equal(parse.QuantitySpan, scaled.QuantitySpan);
        Assert.False(scaled.IsRounded);
        Assert.Null(scaled.Unscaled);
    }

    [Fact]
    public void An_unparsed_line_is_as_written_and_flagged() =>
        AssertAsWritten("1 cup + 2 tbsp flour", ScaleFactor.Doubled, UnscaledReason.Unparsed);

    [Theory]
    [InlineData("350°F oven")]
    [InlineData("180 degrees C oven")]
    public void A_temperature_is_never_scaled_or_flagged(string text) => AssertAsWritten(text, ScaleFactor.Tripled, null);

    [Theory]
    [InlineData("salt")]
    [InlineData("salt, to taste")]
    [InlineData("kosher salt (to taste)")]
    [InlineData("a large egg")]
    [InlineData("butter, softened to 65°F")]
    [InlineData("whole milk (3.25%)")]
    public void A_line_with_nothing_to_scale_is_as_written_and_unflagged(string text) => AssertAsWritten(text, ScaleFactor.Doubled, null);

    [Theory]
    [InlineData("Juice of 2 lemons")]
    [InlineData("Juice of two lemons")]
    [InlineData("Zest of ½ lemon")]
    [InlineData("salt to taste (about 1 tsp)")]
    [InlineData("1 cup (120 g) flour")]
    [InlineData("2 eggs, one beaten")]
    [InlineData("2 eggs plus 1 yolk")]
    [InlineData("1 can (15 oz) black beans")]
    [InlineData("2 cups flour (about 250 g)")]
    [InlineData("1 cup water (２ cups if dry)")]
    public void A_line_with_another_amount_is_as_written_and_flagged(string text) =>
        AssertAsWritten(text, ScaleFactor.Doubled, UnscaledReason.OtherAmounts);

    [Theory]
    [InlineData("1 (14 oz) can tomatoes", "2 (14 oz) cans tomatoes")]
    [InlineData("1 14-ounce can coconut milk", "2 14-ounce cans coconut milk")]
    [InlineData("1 9-inch pie crust", "2 9-inch pie crust")]
    [InlineData("1 (about 2 lb) butternut squash", "2 (about 2 lb) butternut squash")]
    [InlineData("1 cup 2% milk", "2 cups 2% milk")]
    [InlineData("1 cup milk (3.25%)", "2 cups milk (3.25%)")]
    [InlineData("1 cup water, heated to 110 °F", "2 cups water, heated to 110 °F")]
    [InlineData("1 cup water (110°F)", "2 cups water (110°F)")]
    [InlineData("1 cup water (105-115°F)", "2 cups water (105-115°F)")]
    [InlineData("1 cup water (105 - 115 °F)", "2 cups water (105 - 115 °F)")]
    [InlineData("1 cup water (40–45 degrees C)", "2 cups water (40–45 degrees C)")]
    [InlineData("1 dozen eggs", "2 dozen eggs")]
    public void A_size_a_percentage_or_a_temperature_is_not_another_amount(string text, string doubled) =>
        Assert.Equal(doubled, Scale(text, ScaleFactor.Doubled).Text);

    // "2 ½-inch" reads as 2½: the new line is read again, and a size's fraction that would join the new amount is caught.
    [Theory]
    [InlineData("a ½-inch piece ginger")]
    [InlineData("one 1/2-inch piece ginger")]
    [InlineData("a 1/2-inch slice ginger")]
    public void A_size_that_would_join_the_new_amount_is_another_amount(string text)
    {
        AssertAsWritten(text, ScaleFactor.Doubled, UnscaledReason.OtherAmounts);
        Assert.Null(Scale(text, ScaleFactor.Halved).Unscaled);
        Assert.NotEqual(text, Scale(text, ScaleFactor.Halved).Text);
    }

    // A line at the stored length grows when "cup" becomes "cups"; the read-back still reads it.
    [Fact]
    public void A_line_that_grows_past_the_stored_length_is_still_scaled()
    {
        var core = "1 cup " + new string('a', RecipeLimits.MaxIngredientItemLength) + " (" + new string('b', RecipeLimits.MaxIngredientNoteLength) + ")";
        var text = core + new string(' ', RecipeLimits.MaxIngredientTextLength - core.Length);
        Assert.Equal(IngredientParseStatus.Parsed, IngredientLineParser.Parse(text).Status);

        var scaled = Scale(text, ScaleFactor.Doubled);

        Assert.Equal(RecipeLimits.MaxIngredientTextLength + 1, scaled.Text.Length);
        Assert.StartsWith("2 cups aaa", scaled.Text, StringComparison.Ordinal);
        Assert.Null(scaled.Unscaled);
    }

    [Fact]
    public void An_amount_past_the_stored_cap_is_shown_though_it_could_not_be_stored()
    {
        var scaled = Scale("1000000 g sugar", ScaleFactor.Tripled);

        Assert.Equal("3000000 g sugar", scaled.Text);
        Assert.Null(scaled.Unscaled);
    }

    [Theory]
    [InlineData("1 cup water (105-115 g)")]
    [InlineData("1 cup flour, 2-3 more if sticky")]
    public void A_range_that_is_not_a_temperature_is_another_amount(string text) =>
        AssertAsWritten(text, ScaleFactor.Doubled, UnscaledReason.OtherAmounts);

    [Theory]
    [InlineData("½ cup butter", "¼ cup butter")]
    [InlineData("1 1/2 cups milk", "3/4 cup milk")]
    [InlineData("1-1/2 cups milk", "3/4 cup milk")]
    [InlineData("1 1⁄2 cups milk", "3/4 cup milk")]
    [InlineData("1 ½ cups milk", "¾ cup milk")]
    [InlineData("1.5 cups milk", "0.75 cup milk")]
    [InlineData(".5 cup milk", "0.25 cup milk")]
    [InlineData("3 eggs", "1½ eggs")]
    [InlineData("three eggs", "1½ eggs")]
    [InlineData("a cup of coffee", "½ cup of coffee")]
    [InlineData("½ kg flour", "0.25 kg flour")]
    [InlineData("1/2 l stock", "0.25 l stock")]
    public void An_amount_is_written_the_way_the_blogger_writes_theirs(string text, string halved) =>
        Assert.Equal(halved, Scale(text, ScaleFactor.Halved).Text);

    [Theory]
    [InlineData("2 to 3 cups stock", "1 to 1½ cups stock")]
    [InlineData("2 TO 3 cups stock", "1 TO 1½ cups stock")]
    [InlineData("2-3 cups stock", "1–1½ cups stock")]
    [InlineData("2—3 cups stock", "1–1½ cups stock")]
    [InlineData("2 - 3 cups stock", "1–1½ cups stock")]
    [InlineData("1/2-1 tsp chili flakes", "1/4–1/2 tsp chili flakes")]
    [InlineData("1.5-2 kg beef", "0.75–1 kg beef")]
    public void A_range_keeps_its_to_and_writes_a_dash_as_an_en_dash(string text, string halved) =>
        Assert.Equal(halved, Scale(text, ScaleFactor.Halved).Text);

    // On every grid, with a dash or a "to": the joiner and the second end go with the collapse.
    [Theory]
    [InlineData("1-2 pinches salt", "1 pinch salt", 1L, 1L)]
    [InlineData("1 to 2 pinches salt", "1 pinch salt", 1L, 1L)]
    [InlineData("1/16 to 1/8 tsp cayenne", "1/16 tsp cayenne", 1L, 16L)]
    [InlineData("0.01-0.02 kg flour", "0.05 kg flour", 1L, 20L)]
    public void A_range_whose_ends_round_alike_is_one_amount(string text, string halved, long n, long d)
    {
        var scaled = Scale(text, ScaleFactor.Halved);

        Assert.Equal(halved, scaled.Text);
        Assert.Equal(Quantity.Amount(Rational.Create(n, d)), scaled.Quantity);
        Assert.True(scaled.IsRounded);
        Assert.Null(scaled.Unscaled);
    }

    [Fact]
    public void A_range_whose_ends_fall_in_different_bands_rounds_each_on_its_own()
    {
        var scaled = Scale("7-9 cups water", ScaleFactor.Of(Rational.Create(5, 4)));

        Assert.Equal("8¾–11½ cups water", scaled.Text);
        Assert.True(scaled.IsRounded);
    }

    // Which fractions a blogger writes is read from the span: a fraction character anywhere means characters, and a decimal
    // writer's rare sixteenth is ASCII with the other end, never a mix.
    [Theory]
    [InlineData("1/2–¾ cup sugar", "¼–⅜ cup sugar")]
    [InlineData("1-½ cups milk", "¾ cup milk")]
    [InlineData("0.125-0.25 tsp salt", "1/16–1/8 tsp salt")]
    [InlineData("0.125-0.5 tsp salt", "1/16–1/4 tsp salt")]
    [InlineData("0.125 tsp salt", "1/16 tsp salt")]
    [InlineData("1 to 2 kg flour", "0.5 to 1 kg flour")]
    [InlineData("one litre water", "0.5 litre water")]
    [InlineData("1-1.5 cups milk", "0.5–0.75 cup milk")]
    [InlineData("1.5 to 2 cups milk", "0.75 to 1 cup milk")]
    [InlineData("1 cups sugar", "½ cup sugar")]
    public void The_style_and_grid_follow_the_span_and_the_unit(string text, string halved) =>
        Assert.Equal(halved, Scale(text, ScaleFactor.Halved).Text);

    [Theory]
    [InlineData("0.5 pinch salt", "1 pinch salt")]
    [InlineData("2 cup sugar", "4 cups sugar")]
    [InlineData("500000 g sugar", "1000000 g sugar")]
    public void More_corners_doubled(string text, string doubled) =>
        Assert.Equal(doubled, Scale(text, ScaleFactor.Doubled).Text);

    // A temperature line is answered before other numbers are looked for, on purpose: it is never scaled at all.
    [Fact]
    public void A_temperature_line_with_another_number_is_still_unflagged() =>
        AssertAsWritten("180°C oven, 2 racks", ScaleFactor.Doubled, null);

    [Theory]
    [InlineData("milk (2-3%)")]
    [InlineData("1 cup milk, warmed to 37.5°C")]
    public void A_percentage_or_temperature_range_or_decimal_is_not_another_amount(string text) =>
        Assert.Null(Scale(text, ScaleFactor.Doubled).Unscaled);

    [Theory]
    [InlineData("milk (2 %)")]
    [InlineData("1 cup flour, about 2")]
    [InlineData("1 cup flour, about 2 ")]
    [InlineData("1 cup water (105- warm)")]
    public void A_number_that_only_looks_descriptive_is_another_amount(string text) =>
        AssertAsWritten(text, ScaleFactor.Doubled, UnscaledReason.OtherAmounts);

    [Theory]
    [InlineData("a pinch of salt")]
    [InlineData("1 pinch salt")]
    [InlineData("1/16 tsp cayenne")]
    [InlineData("1/4 tsp salt")] // 3/16 rounds back up to 1/4
    public void An_amount_that_rounds_back_to_the_bloggers_is_as_written(string text) =>
        AssertAsWritten(text, ThreeQuarters, null);

    [Theory]
    [InlineData("a pinch of salt", "2 pinches of salt")]
    [InlineData("1 Cup flour", "2 Cups flour")]
    [InlineData("1 CUP flour", "2 CUPS flour")]
    [InlineData("1 fluid  ounce rum", "2 fluid  ounces rum")]
    [InlineData("1 tbsp oil", "2 tbsp oil")]
    [InlineData("1 lb beef", "2 lb beef")]
    [InlineData("1 egg", "2 egg")]
    public void Only_a_listed_unit_word_agrees_in_number(string text, string doubled) =>
        Assert.Equal(doubled, Scale(text, ScaleFactor.Doubled).Text);

    [Theory]
    [InlineData("2 cups flour", "1 cup flour")]
    [InlineData("3 cups flour", "1½ cups flour")]
    [InlineData("1 1/2 cups flour", "3/4 cup flour")]
    [InlineData("1-2 cups flour", "½–1 cup flour")]
    [InlineData("2-3 cups flour", "1–1½ cups flour")]
    public void A_unit_is_singular_up_to_one_and_plural_above(string text, string halved) =>
        Assert.Equal(halved, Scale(text, ScaleFactor.Halved).Text);

    [Fact]
    public void The_result_says_what_it_shows_and_where()
    {
        var scaled = Scale("• 1½ cups milk", ScaleFactor.Of(Rational.Create(5, 4)));

        Assert.Equal("• 1⅞ cups milk", scaled.Text);
        Assert.Equal(Quantity.Amount(Rational.Create(15, 8)), scaled.Quantity);
        Assert.Equal(new TextSpan(2, 2), scaled.QuantitySpan);
        Assert.Equal("1⅞", scaled.Text.Substring(scaled.QuantitySpan!.Value.Start, scaled.QuantitySpan.Value.Length));
        Assert.False(scaled.IsRounded);
        Assert.Null(scaled.Unscaled);
    }

    [Fact]
    public void Rounding_is_reported()
    {
        Assert.True(Scale("250 g flour", ThreeQuarters).IsRounded);
        Assert.False(Scale("250 g flour", ScaleFactor.Halved).IsRounded);
        Assert.Equal(Quantity.Amount(190), Scale("250 g flour", ThreeQuarters).Quantity);
    }

    [Fact]
    public void The_original_text_is_never_changed_and_only_its_spans_are_written_anew()
    {
        const string text = "  2 CUPS of flour,  sifted (twice!)  ";
        var scaled = Scale(text, ScaleFactor.Tripled);

        Assert.Equal("  6 CUPS of flour,  sifted (twice!)  ", scaled.Text);
    }

    [Fact]
    public void A_yield_scales_on_the_kitchen_grid_with_fraction_characters()
    {
        Assert.Equal(new ScaledYield(Quantity.Amount(12), "12", false), RecipeScaler.ScaleYield(Quantity.Amount(24), ScaleFactor.Halved));
        Assert.Equal(new ScaledYield(Quantity.Amount(Rational.Create(3, 2)), "1½", false), RecipeScaler.ScaleYield(Quantity.Amount(3), ScaleFactor.Halved));
        Assert.Equal(new ScaledYield(Quantity.Range(8, 12), "8–12", false), RecipeScaler.ScaleYield(Quantity.Range(4, 6), ScaleFactor.Doubled));
        Assert.Equal(new ScaledYield(Quantity.Amount(4), "4", false), RecipeScaler.ScaleYield(Quantity.Amount(4), ScaleFactor.Unchanged));
        Assert.Equal(new ScaledYield(Quantity.Amount(Rational.Create(17, 8)), "2⅛", true), RecipeScaler.ScaleYield(Quantity.Amount(3), ScaleFactor.Of(Rational.Create(5, 7))));
        Assert.Equal(new ScaledYield(Quantity.Amount(3), "3", false), RecipeScaler.ScaleYield(Quantity.Amount(4), ScaleFactor.ForServings(Quantity.Amount(4), 3)!));
    }

    [Fact]
    public void A_yield_at_the_edges()
    {
        Assert.Equal(
            new ScaledYield(Quantity.Amount(Rational.Create(1, 16)), "1/16", true),
            RecipeScaler.ScaleYield(Quantity.Range(1, 2), ScaleFactor.Of(Rational.Create(1, 64))));
        Assert.Equal(new ScaledYield(Quantity.Amount(Rational.Create(3, 2)), "1½", false), RecipeScaler.ScaleYield(Quantity.Amount(Rational.Create(3, 2)), ScaleFactor.Unchanged));
        Assert.Equal("1000000000000", RecipeScaler.ScaleYield(Quantity.Amount(RecipeLimits.MaxQuantityTerm), ScaleFactor.Of(RecipeLimits.MaxQuantityTerm)).Text);
    }

    [Fact]
    public void A_yield_is_never_to_taste() =>
        Assert.Equal("yield", Assert.Throws<ArgumentException>(() => RecipeScaler.ScaleYield(Quantity.ToTaste, ScaleFactor.Doubled)).ParamName);

    [Fact]
    public void A_line_is_taken_from_its_parse()
    {
        const string text = "2 cups (8 oz) flour, sifted";
        var parse = IngredientLineParser.Parse(text);
        var line = ScalableIngredientLine.From(text, parse);

        Assert.Equal(
            new ScalableIngredientLine(text, IngredientParseStatus.Parsed, parse.Quantity, parse.QuantitySpan, "cup", parse.UnitSpan, "flour"),
            line);
    }

    public static TheoryData<ScalableIngredientLine> Malformed => new()
    {
        new("2 cups flour", IngredientParseStatus.Parsed, Quantity.Amount(2), null, null, null, "cups flour"),
        new("2 cups flour", IngredientParseStatus.Parsed, Quantity.Amount(2), new TextSpan(0, 1), "teacup", new TextSpan(2, 4), "flour"),
        new("2 cups flour", IngredientParseStatus.Parsed, Quantity.Amount(2), new TextSpan(0, 1), "cup", null, "flour"),
        new("2 cups flour", IngredientParseStatus.Parsed, Quantity.Amount(2), new TextSpan(0, 1), null, new TextSpan(2, 4), "flour"),
        new("2 cups flour", IngredientParseStatus.Parsed, Quantity.Amount(2), new TextSpan(0, 13), null, null, "flour"),
        new("2 cups flour", IngredientParseStatus.Parsed, Quantity.Amount(2), new TextSpan(-1, 1), null, null, "flour"),
        new("2 cups flour", IngredientParseStatus.Parsed, Quantity.Amount(2), new TextSpan(0, 0), null, null, "flour"),
        new("2 cups flour", IngredientParseStatus.Parsed, Quantity.Amount(2), new TextSpan(2, 4), "cup", new TextSpan(0, 1), "flour"),
        new("cups flour", IngredientParseStatus.Parsed, null, null, "cup", new TextSpan(0, 4), "flour"),
        new("2 cups flour", IngredientParseStatus.Parsed, Quantity.Amount(RecipeLimits.MaxQuantityTerm + 1), new TextSpan(0, 1), null, null, "flour"),
        new("2 cups flour", IngredientParseStatus.Parsed, Quantity.Amount(Rational.Create(1, RecipeLimits.MaxQuantityTerm + 1)), new TextSpan(0, 1), null, null, "flour"),
        new("2 cups flour", IngredientParseStatus.Parsed, Quantity.Range(1, RecipeLimits.MaxQuantityTerm + 1), new TextSpan(0, 1), null, null, "flour"),
        new("1 cup + 2 tbsp flour", IngredientParseStatus.Unparsed, Quantity.Amount(1), new TextSpan(0, 1), null, null, null),
        new("1 cup + 2 tbsp flour", IngredientParseStatus.Unparsed, null, null, null, null, "flour"),
        new("Juice of 2 lemons", IngredientParseStatus.Parsed, null, new TextSpan(9, 1), null, null, "Juice of 2 lemons"),
        new("cup salt to taste", IngredientParseStatus.Parsed, Quantity.ToTaste, new TextSpan(9, 8), "cup", new TextSpan(0, 3), "salt"),
        new(null!, IngredientParseStatus.Parsed, null, null, null, null, "salt"),
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void A_line_the_parser_could_not_have_read_is_refused(ScalableIngredientLine line) =>
        Assert.ThrowsAny<ArgumentException>(() => RecipeScaler.ScaleLine(line, ScaleFactor.Doubled));

    [Fact]
    public void Nothing_is_missing()
    {
        Assert.Throws<ArgumentNullException>(() => RecipeScaler.ScaleLine(null!, ScaleFactor.Doubled));
        Assert.Throws<ArgumentNullException>(() => RecipeScaler.ScaleLine(Line("salt"), null!));
        Assert.Throws<ArgumentNullException>(() => RecipeScaler.ScaleYield(null!, ScaleFactor.Doubled));
        Assert.Throws<ArgumentNullException>(() => RecipeScaler.ScaleYield(Quantity.Amount(1), null!));
    }

    // Page data will carry the reason as kebab-case (PP7.11), so its members are pinned.
    [Fact]
    public void The_reasons_are_pinned() =>
        Assert.Equal(["Unparsed", "OtherAmounts"], Enum.GetNames<UnscaledReason>());

    private static ScalableIngredientLine Line(string text) => ScalableIngredientLine.From(text, IngredientLineParser.Parse(text));

    private static ScaledIngredientLine Scale(string text, ScaleFactor factor) => RecipeScaler.ScaleLine(Line(text), factor);

    private static void AssertAsWritten(string text, ScaleFactor factor, UnscaledReason? reason)
    {
        var parse = IngredientLineParser.Parse(text);
        Assert.Equal(new ScaledIngredientLine(text, parse.Quantity, parse.QuantitySpan, false, reason), Scale(text, factor));
    }
}
