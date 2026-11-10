using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.8: each rule of RecipeConverter and its edges (RCP-005, RCP-002). The golden table shows real lines; these pin the
// thresholds, the styles, the temperature rules, and every refusal.
public sealed class RecipeConverterTests
{
    private const UnitSystem Metric = UnitSystem.Metric;
    private const UnitSystem Us = UnitSystem.UsCustomary;

    // The target unit is chosen by the exact amount before rounding, either side of each threshold; rounding never changes
    // it (2.2 lb is 997.9 g, which rounds to 1000 g, still in grams).
    [Theory]
    [InlineData("14 ml water", Us, "2⅞ tsp water")]
    [InlineData("15 ml water", Us, "1 tbsp water")]
    [InlineData("59 ml water", Us, "4 tbsp water")]
    [InlineData("60 ml water", Us, "¼ cup water")]
    [InlineData("999 ml water", Us, "4¼ cups water")]
    [InlineData("4 cups water", Metric, "945 milliliters water")]
    [InlineData("4.25 cups water", Metric, "1 liter water")]
    [InlineData("453 g potatoes", Us, "16 oz potatoes")]
    [InlineData("454 g potatoes", Us, "1 lb potatoes")]
    [InlineData("2.2 lb potatoes", Metric, "1000 g potatoes")]
    [InlineData("2.25 lb potatoes", Metric, "1 kg potatoes")]
    [InlineData("35 oz potatoes", Metric, "990 g potatoes")]
    public void The_target_unit_is_chosen_by_the_exact_amount(string input, UnitSystem target, string expected) =>
        Assert.Equal(expected, Convert(input, target).Text);

    // Volume and weight cross only for a listed item, and only in the direction a cook in that system measures.
    [Theory]
    [InlineData("1 cup flour", Metric, "120 grams flour")]
    [InlineData("1 cup FLOUR", Metric, "120 grams FLOUR")]
    [InlineData("1 cup  all-purpose   flour", Metric, "120 grams  all-purpose   flour")]
    [InlineData("1 cup kale", Metric, "235 milliliters kale")]
    [InlineData("1 cup flour blend", Metric, "235 milliliters flour blend")]
    [InlineData("120 g flour", Us, "1 cup flour")]
    [InlineData("120 g kale", Us, "4¼ oz kale")]
    [InlineData("1 lb butter", Metric, "455 g butter")]
    [InlineData("250 ml honey", Us, "1 cup honey")]
    public void A_density_is_used_only_when_the_item_is_listed(string input, UnitSystem target, string expected) =>
        Assert.Equal(expected, Convert(input, target).Text);

    // A spelled-out unit becomes the target's word, agreeing with the amount; an abbreviation the target's abbreviation, or
    // its word when it has none. A word is never joined to its number.
    [Theory]
    [InlineData("1 cup milk", Metric, "235 milliliters milk")]
    [InlineData("1 c milk", Metric, "235 ml milk")]
    [InlineData("1c milk", Metric, "235ml milk")]
    [InlineData("1 Cup milk", Metric, "235 milliliters milk")]
    [InlineData("250 ml milk", Us, "1 cup milk")]
    [InlineData("250ml milk", Us, "1 cup milk")]
    [InlineData("500 ml milk", Us, "2⅛ cups milk")]
    [InlineData("1 litre milk", Us, "4¼ cups milk")]
    [InlineData("15 millilitres oil", Us, "1 tablespoon oil")]
    [InlineData("30 millilitres oil", Us, "2 tablespoons oil")]
    [InlineData("28 grams chocolate", Us, "1 ounce chocolate")]
    [InlineData("56 grams chocolate", Us, "2 ounces chocolate")]
    [InlineData("1 fl. oz. rum", Metric, "30 ml rum")]
    [InlineData("1 fluid ounce rum", Metric, "30 milliliters rum")]
    [InlineData("1 qt stock", Metric, "945 ml stock")]
    [InlineData("1 kilo potatoes", Us, "2¼ pounds potatoes")]
    [InlineData("½ liter milk", Us, "2⅛ cups milk")]
    [InlineData("1\u00A0cup milk", Metric, "235\u00A0milliliters milk")]
    [InlineData("1  cup milk", Metric, "235  milliliters milk")]
    public void The_unit_keeps_the_bloggers_style(string input, UnitSystem target, string expected) =>
        Assert.Equal(expected, Convert(input, target).Text);

    // Metric is written in decimals; US in fractions, ASCII when the blogger's were, and never with a decimal point.
    [Theory]
    [InlineData("1 1/2 l water", Us, "6 1/3 cups water")]
    [InlineData("1½ l water", Us, "6⅓ cups water")]
    [InlineData("1.5 l water", Us, "6⅓ cups water")]
    [InlineData("1/3 cup oil", Metric, "79 milliliters oil")]
    [InlineData("1/3 c oil", Metric, "79 ml oil")]
    [InlineData("1 gallon water", Metric, "3.8 liters water")]
    public void Metric_is_decimal_and_us_is_fractions(string input, UnitSystem target, string expected) =>
        Assert.Equal(expected, Convert(input, target).Text);

    // A range's unit is chosen by its low end, so both ends share it; ends that round alike are one amount.
    [Theory]
    [InlineData("4-5 cups stock", Metric, "945–1185 milliliters stock")]
    [InlineData("3-4 cups stock", Metric, "710–945 milliliters stock")]
    [InlineData("900-1100 g potatoes", Us, "2–2⅜ lb potatoes")]
    [InlineData("1 to 2 litres water", Us, "4¼ to 8½ cups water")]
    [InlineData("236-237 ml water", Us, "1 cup water")]
    public void A_range_shares_one_unit(string input, UnitSystem target, string expected) =>
        Assert.Equal(expected, Convert(input, target).Text);

    [Fact]
    public void A_range_that_collapses_is_one_rounded_amount()
    {
        var line = Convert("236-237 ml water", Us);
        Assert.Equal(Quantity.Amount(1), line.Quantity);
        Assert.True(line.IsRounded);
        Assert.True(line.IsConverted);
    }

    // The amount is scaled exactly, converted exactly, and rounded once: half of 100 g of butter is 50 g, which is 3.53 tbsp
    // and shows 3½ tbsp, where rounding 100 g to ½ cup first and halving it would show ¼ cup (4 tbsp).
    [Fact]
    public void A_scaled_amount_is_converted_before_it_is_rounded()
    {
        Assert.Equal("½ cup butter", Convert("100 g butter", Us).Text);
        var line = Convert("100 g butter", Us, ScaleFactor.Halved);
        Assert.Equal("3½ tbsp butter", line.Text);
        Assert.Equal(Quantity.Amount(Rational.Create(7, 2)), line.Quantity);
        Assert.True(line.IsRounded);
    }

    [Fact]
    public void An_exact_conversion_is_not_rounded()
    {
        var line = Convert("2 cups flour", Metric);
        Assert.Equal(Quantity.Amount(240), line.Quantity);
        Assert.False(line.IsRounded);
        Assert.True(line.IsConverted);
        Assert.Equal(new TextSpan(0, 3), line.QuantitySpan);
    }

    // Spoons stay as written in metric, scaled like any line; they convert into US from ml.
    [Theory]
    [InlineData("1 tablespoon olive oil")]
    [InlineData("2 tsp flour")]
    [InlineData("1 T butter")]
    public void Spoons_stay_in_metric(string input)
    {
        Assert.Equal(ConversionCorpus.Convert(input, ScaleFactor.Doubled, Metric), ScalingCorpus.Scale(input, ScaleFactor.Doubled));
        Assert.False(Convert(input, Metric).IsConverted);
    }

    // A line already in the target system, a count, and a line with no unit are exactly what the scaler shows.
    [Theory]
    [InlineData("2 cups flour", Us)]
    [InlineData("500 g flour", Metric)]
    [InlineData("3 large eggs", Metric)]
    [InlineData("2 cloves garlic", Us)]
    [InlineData("1 (14 oz) can tomatoes", Metric)]
    [InlineData("Juice of 2 lemons", Metric)]
    [InlineData("1 cup (240 ml) milk", Us)]
    [InlineData("salt, to taste", Metric)]
    public void A_line_that_does_not_convert_is_the_scalers(string input, UnitSystem target)
    {
        foreach (var factor in ScaleFactor.Multipliers)
        {
            Assert.Equal(ScalingCorpus.Scale(input, factor), ConversionCorpus.Convert(input, factor, target));
        }
    }

    // A line whose measure would convert but has another number is flagged even at 1x, so the reader knows it was not converted.
    [Theory]
    [InlineData("1 cup (240 ml) milk", Metric)]
    [InlineData("1 cup (2 sticks) butter", Metric)]
    [InlineData("1 lb (450 g) beef", Metric)]
    [InlineData("1 cup plus 2 tablespoons flour", Metric)]
    [InlineData("250 ml (1 cup) milk", Us)]
    [InlineData("200 g (7 oz) chocolate", Us)]
    public void Other_amounts_stop_a_measure_converting(string input, UnitSystem target)
    {
        var line = Convert(input, target);
        if (IngredientLineParser.Parse(input).Status == IngredientParseStatus.Unparsed)
        {
            Assert.Equal(UnscaledReason.Unparsed, line.Unscaled);
        }
        else
        {
            Assert.Equal(UnscaledReason.OtherAmounts, line.Unscaled);
        }

        Assert.Equal(input, line.Text);
        Assert.False(line.IsConverted);
    }

    // A size between the amount and the unit would be left in the other system.
    [Fact]
    public void A_size_before_a_convertible_unit_is_flagged()
    {
        var input = "1 (8 oz) cup yogurt";
        Assert.Equal("cup", IngredientLineParser.Parse(input).UnitCode);
        var line = Convert(input, Metric);
        Assert.Equal(input, line.Text);
        Assert.Equal(UnscaledReason.OtherAmounts, line.Unscaled);
    }

    [Fact]
    public void An_unparsed_line_is_flagged_in_every_converted_variant()
    {
        var input = "1 cup + 2 tbsp flour";
        Assert.Equal(IngredientParseStatus.Unparsed, IngredientLineParser.Parse(input).Status);
        foreach (var target in new[] { Metric, Us })
        {
            var line = Convert(input, target);
            Assert.Equal(input, line.Text);
            Assert.Equal(UnscaledReason.Unparsed, line.Unscaled);
        }
    }

    // A temperature line converts at any scale, because temperatures never scale.
    [Theory]
    [InlineData("350°F oven", "175°C oven", 3)]
    [InlineData("105-115°F water", "41–46°C water", 5)]
    [InlineData("105 to 115 degrees F water", "41 to 46 degrees C water", 8)]
    [InlineData("110 °F water", "43 °C water", 2)]
    public void A_temperature_line_converts_and_never_scales(string input, string expected, int quantityLength)
    {
        foreach (var factor in ScaleFactor.Multipliers)
        {
            var line = Convert(input, Metric, factor);
            Assert.Equal(expected, line.Text);
            Assert.True(line.IsConverted);
            Assert.True(line.IsRounded);
            Assert.Equal(new TextSpan(0, quantityLength), line.QuantitySpan);
        }
    }

    // The scaler changes the measure's length, and temperatures after it still convert, every one of them.
    [Theory]
    [InlineData("1 tablespoon water, warmed to 110°F", 2, 1, Metric, "2 tablespoons water, warmed to 43°C", true)]
    [InlineData("10 eggs, at 70°F", 1, 2, Metric, "5 eggs, at 21°C", true)]
    [InlineData("10 eggs, at 70°F", 3, 4, Metric, "7½ eggs, at 21°C", true)]
    [InlineData("1 litre milk, scalded to 180°F", 2, 1, Metric, "2 litres milk, scalded to 82°C", true)]
    [InlineData("12 eggs, at 70°F or 21°C", 1, 2, Metric, "6 eggs, at 70°F or 21°C", false)]
    [InlineData("2 cups milk, at 40°C (warm)", 1, 2, Us, "1 cup milk, at 104°F (warm)", true)]
    public void Temperatures_after_a_scaled_measure_still_convert(
        string input, long numerator, long denominator, UnitSystem target, string expected, bool converted)
    {
        var line = Convert(input, target, ScaleFactor.Of(Rational.Create(numerator, denominator)));
        Assert.Equal(expected, line.Text);
        Assert.Equal(converted, line.IsConverted);
        Assert.Null(line.Unscaled);
    }

    // A temperature line: exact conversions are not rounded, a range whose ends round alike is one amount, a line that
    // already names the target stays as written, and a temperature the reader cannot place (a fraction) is left alone.
    [Fact]
    public void Temperature_line_edges()
    {
        var boiling = Convert("212°F water", Metric);
        Assert.Equal("100°C water", boiling.Text);
        Assert.False(boiling.IsRounded);
        Assert.Equal(Quantity.Amount(100), boiling.Quantity);

        var back = Convert("100°C water", Us);
        Assert.Equal("212°F water", back.Text);
        Assert.False(back.IsRounded);

        var collapsed = Convert("356-358°F oil", Metric);
        Assert.Equal("180°C oil", collapsed.Text);
        Assert.Equal(Quantity.Amount(180), collapsed.Quantity);
        Assert.Equal(new TextSpan(0, 3), collapsed.QuantitySpan);
        Assert.True(collapsed.IsRounded);

        foreach (var target in new[] { Metric, Us })
        {
            var both = Convert("350°F (180°C) oven", target);
            Assert.Equal("350°F (180°C) oven", both.Text);
            Assert.False(both.IsConverted);
            Assert.Null(both.Unscaled);
        }

        var fraction = Convert("98½°F water", Metric);
        Assert.Equal(Quantity.Amount(Rational.Create(197, 2)), IngredientLineParser.Parse("98½°F water").Quantity);
        Assert.Equal("98½°F water", fraction.Text);
        Assert.False(fraction.IsConverted);
    }

    // A converted temperature that is not above zero cannot be a line's quantity, so the line stays as written.
    [Fact]
    public void A_temperature_line_at_or_below_zero_stays_as_written()
    {
        var line = Convert("20°F brine", Metric);
        Assert.Equal("20°F brine", line.Text);
        Assert.False(line.IsConverted);
        Assert.Null(line.Unscaled);
        Assert.Equal("32°F brine", Convert("32°F brine", Metric).Text);
    }

    // Temperatures in the rest of a line convert with the measure, or alone on a line whose measure does not convert.
    [Theory]
    [InlineData("1 cup water, heated to 110°F", Metric, "235 milliliters water, heated to 43°C")]
    [InlineData("2 eggs, at 70°F", Metric, "2 eggs, at 21°C")]
    [InlineData("butter, softened to 65°F", Metric, "butter, softened to 18°C")]
    [InlineData("250 ml water, at 40°C", Us, "1 cup water, at 104°F")]
    [InlineData("1 cup water (110°F/43°C)", Metric, "235 milliliters water (110°F/43°C)")]
    [InlineData("2 eggs (70°F/21°C)", Metric, "2 eggs (70°F/21°C)")]
    [InlineData("2 cups water, chilled to 28°F", Metric, "475 milliliters water, chilled to -2°C")]
    public void Temperatures_in_a_line_convert_together(string input, UnitSystem target, string expected) =>
        Assert.Equal(expected, Convert(input, target).Text);

    // Rounding: whole degrees below 100 °C or 215 °F, fives at and above; a tie goes to the warmer.
    [Theory]
    [InlineData(99, 2, Metric, 50)]
    [InlineData(199, 2, Metric, 100)]
    [InlineData(-1, 2, Metric, 0)]
    [InlineData(-37, 2, Metric, -18)]
    [InlineData(1001, 10, Metric, 100)]
    [InlineData(1024, 10, Metric, 100)]
    [InlineData(1025, 10, Metric, 105)]
    [InlineData(530, 3, Metric, 175)]
    [InlineData(2145, 10, Us, 215)]
    [InlineData(2144, 10, Us, 214)]
    [InlineData(212, 1, Us, 212)]
    [InlineData(2175, 10, Us, 220)]
    [InlineData(356, 1, Us, 355)]
    public void A_temperature_rounds_to_whole_degrees_then_fives(long numerator, long denominator, UnitSystem scale, long expected) =>
        Assert.Equal((Rational)expected, RecipeConverter.RoundTemperature(Rational.Create(numerator, denominator), scale));

    [Fact]
    public void Temperature_rounding_is_monotone_across_both_thresholds()
    {
        foreach (var scale in new[] { Metric, Us })
        {
            var previous = RecipeConverter.RoundTemperature(-50, scale);
            for (var tenths = -499; tenths <= 6000; tenths++)
            {
                var rounded = RecipeConverter.RoundTemperature(Rational.Create(tenths, 10), scale);
                Assert.True(rounded >= previous, $"{tenths}/10 in {scale} rounds below the value before it.");
                previous = rounded;
            }
        }
    }

    [Theory]
    [InlineData(212, "fahrenheit", 100)]
    [InlineData(32, "fahrenheit", 0)]
    [InlineData(100, "celsius", 212)]
    [InlineData(180, "celsius", 356)]
    public void A_temperature_converts_exactly(long value, string from, long expected) =>
        Assert.Equal((Rational)expected, RecipeConverter.ConvertTemperature(value, from));

    // Every temperature spelling, in either case, has a counterpart that is a whole spelling of the other scale.
    [Fact]
    public void Every_temperature_spelling_has_a_counterpart_in_the_other_scale()
    {
        foreach (var alias in Units.Aliases.Where(a => a.UnitCode is "fahrenheit" or "celsius"))
        {
            foreach (var written in new[] { alias.Alias, alias.Alias.ToUpperInvariant() })
            {
                var counterpart = RecipeConverter.Counterpart(written, alias.UnitCode);
                var match = UnitSpellings.Match(counterpart, 0);
                Assert.True(match is not null && match.Value.Length == counterpart.Length, $"'{written}' becomes '{counterpart}'.");
                Assert.NotEqual(alias.UnitCode, match!.Value.Code);
            }
        }

        Assert.Equal("°C", RecipeConverter.Counterpart("°F", "fahrenheit"));
        Assert.Equal("degrees c", RecipeConverter.Counterpart("degrees f", "fahrenheit"));
        Assert.Equal("Celsius", RecipeConverter.Counterpart("Fahrenheit", "fahrenheit"));
        Assert.Equal("Fahrenheit", RecipeConverter.Counterpart("Centigrade", "celsius"));
        Assert.Equal("ºF", RecipeConverter.Counterpart("ºC", "celsius"));
    }

    // Steps: only temperatures, together or not at all.
    [Theory]
    [InlineData("Preheat the oven to 350°F.", Metric, "Preheat the oven to 175°C.")]
    [InlineData("Bake at 160°F - 165°F.", Metric, "Bake at 71°C - 74°C.")]
    [InlineData("Heat to 1,350°F.", Metric, null)]
    [InlineData("Heat to 3.5°C.", Us, "Heat to 38°F.")]
    [InlineData("Freeze at -18°C.", Us, null)]
    [InlineData("Freeze at 0°F, or -18°C.", Metric, null)]
    [InlineData("Freeze at 0°F.", Metric, "Freeze at -18°C.")]
    [InlineData("Keep at 4-6°F.", Metric, null)]
    [InlineData("Keep at 30-40°F.", Metric, null)]
    [InlineData("Keep at 33-40°F.", Metric, "Keep at 1–4°C.")]
    [InlineData("Keep at 32-40°F.", Metric, "Keep at 0–4°C.")]
    [InlineData("Bake at 160°F -165°F.", Metric, "Bake at 71°C -74°C.")]
    [InlineData("Bake at 160°F-165°F.", Metric, null)]
    [InlineData("Heat to 325 - 350°F.", Metric, "Heat to 165–175°C.")]
    [InlineData("Heat to 325 To 350°F.", Metric, "Heat to 165 To 175°C.")]
    [InlineData("Use an A350°F oven.", Metric, null)]
    [InlineData("Heat to 350°F and 1/2 cup at 180°C.", Us, null)]
    [InlineData("Heat to 350 °F.", Us, null)]
    [InlineData("Heat to 350 degrees.", Metric, null)]
    [InlineData("Heat to 350F.", Metric, null)]
    [InlineData("Heat to 325 to 350 °F.", Metric, "Heat to 165 to 175 °C.")]
    [InlineData("Heat to 350–325°F.", Metric, null)]
    [InlineData("Heat to 12345678°F.", Metric, null)]
    [InlineData("Heat to 98.6°F.", Metric, "Heat to 37°C.")]
    [InlineData("Heat to 98.1234567°F.", Metric, null)]
    [InlineData("Mix 2 cups flour.", Metric, null)]
    [InlineData("", Metric, null)]
    public void A_step_converts_its_temperatures_together(string input, UnitSystem target, string? expected)
    {
        var step = RecipeConverter.ConvertStep(input, target);
        Assert.Equal(expected ?? input, step.Text);
        Assert.Equal(expected is not null, step.IsConverted);
    }

    [Fact]
    public void Refusals()
    {
        var line = ScalableIngredientLine.From("1 cup flour", IngredientLineParser.Parse("1 cup flour"));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecipeConverter.ConvertLine(line, ScaleFactor.Unchanged, UnitSystem.Neutral));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecipeConverter.ConvertLine(line, ScaleFactor.Unchanged, (UnitSystem)9));
        Assert.Throws<ArgumentNullException>(() => RecipeConverter.ConvertLine(null!, ScaleFactor.Unchanged, Metric));
        Assert.Throws<ArgumentNullException>(() => RecipeConverter.ConvertLine(line, null!, Metric));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecipeConverter.ConvertStep("Bake at 350°F.", UnitSystem.Neutral));
        Assert.Throws<ArgumentNullException>(() => RecipeConverter.ConvertStep(null!, Metric));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecipeConverter.RoundTemperature(100, UnitSystem.Neutral));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecipeConverter.ConvertTemperature(100, "kelvin"));
        Assert.Throws<ArgumentNullException>(() => RecipeConverter.Counterpart(null!, "fahrenheit"));

        // A line the parser could not have read is a bug, refused as the scaler refuses it.
        var broken = line with { UnitCode = "furlong" };
        Assert.Throws<ArgumentException>(() => RecipeConverter.ConvertLine(broken, ScaleFactor.Unchanged, Metric));
    }

    private static ScaledIngredientLine Convert(string input, UnitSystem target) => Convert(input, target, ScaleFactor.Unchanged);

    private static ScaledIngredientLine Convert(string input, UnitSystem target, ScaleFactor factor) =>
        ConversionCorpus.Convert(input, factor, target);
}
