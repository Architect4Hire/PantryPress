using CsCheck;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.8: the laws the converter keeps over every real line in all three corpora, at many factors, in both systems (RCP-005,
// RCP-002): it never throws and answers the same twice; a line it does not convert is exactly the scaler's; a converted line
// reads back as what it shows, in the target system, with the blogger's words around it untouched; each end is the exact
// conversion rounded once; temperatures ignore the scale. Steps: only temperatures change, all of them, once.
public sealed class RecipeConverterPropertyTests
{
    private const long Iterations = 20_000;

    private static readonly string[] Lines =
    [
        .. IngredientCorpus.Load().Select(c => c.Input)
            .Concat(ScalingCorpus.Load().Select(c => c.Input))
            .Concat(ConversionCorpus.Load().Where(c => !c.IsStep).Select(c => c.Input))
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal),
    ];

    // Each unit's size in ml or g, from its definition, worked out here apart from UnitConversions.
    private static readonly Dictionary<string, Rational> Definitions = new(StringComparer.Ordinal)
    {
        ["teaspoon"] = Rational.Create(3_785_411_784, 768_000_000),
        ["tablespoon"] = Rational.Create(3_785_411_784, 256_000_000),
        ["fluid-ounce"] = Rational.Create(3_785_411_784, 128_000_000),
        ["cup"] = Rational.Create(3_785_411_784, 16_000_000),
        ["pint"] = Rational.Create(3_785_411_784, 8_000_000),
        ["quart"] = Rational.Create(3_785_411_784, 4_000_000),
        ["gallon"] = Rational.Create(3_785_411_784, 1_000_000),
        ["milliliter"] = 1,
        ["deciliter"] = 100,
        ["liter"] = 1_000,
        ["ounce"] = Rational.Create(45_359_237, 1_600_000),
        ["pound"] = Rational.Create(45_359_237, 100_000),
        ["gram"] = 1,
        ["kilogram"] = 1_000,
    };

    private static readonly Gen<ScaleFactor> Factors = Gen.Frequency(
        (2, Gen.OneOfConst([.. ScaleFactor.Multipliers])),
        (2, Gen.Select(Gen.Long[1, 12], Gen.Long[1, 36], (yield, servings) => ScaleFactor.ForServings(Quantity.Amount(yield), servings)!)),
        (1, Gen.Select(Gen.Long[1, 64], Gen.Long[1, 64], (n, d) => ScaleFactor.Of(Rational.Create(n, d)))));

    private static readonly Gen<UnitSystem> Targets = Gen.OneOfConst(UnitSystem.Metric, UnitSystem.UsCustomary);

    private static readonly Gen<(string Text, ScaleFactor Factor, UnitSystem Target)> Cases =
        Gen.Select(Gen.Int[0, Lines.Length - 1].Select(i => Lines[i]), Factors, Targets);

    private static readonly string[] TemperatureSpellings =
        [.. Units.Aliases.Where(a => a.UnitCode is "fahrenheit" or "celsius").Select(a => a.Alias)];

    private static readonly Gen<string> Temperatures = Gen.Select(
        Gen.Int[1, 600],
        Gen.Int[0, 3],
        Gen.Int[1, 60],
        Gen.Int[0, TemperatureSpellings.Length - 1].Select(i => TemperatureSpellings[i]),
        Gen.Bool,
        Gen.Bool,
        (value, range, width, unit, spaced, upper) =>
            value + range switch { 1 => "-" + (value + width), 2 => " to " + (value + width), 3 => " - " + (value + width), _ => string.Empty }
            + (spaced ? " " : string.Empty) + (upper ? unit.ToUpperInvariant() : unit));

    private static readonly Gen<string> Steps = Gen.Select(
        Gen.OneOfConst("Preheat the oven to ", "Bake at ", "Heat the oil to ", "Cook until it reads ", "(", "at", "-", "Chill to "),
        Temperatures,
        Gen.OneOfConst(" for 20 minutes", ", then ", " and 1/2 cup ", "/", " (", " - ", ""),
        Gen.Frequency((2, Temperatures), (1, Gen.Const(string.Empty))),
        (start, first, middle, second) => start + first + middle + second + ".");

    [Fact]
    public void Every_line_converts_without_throwing_and_the_same_twice() =>
        Cases.Sample(iter: Iterations, assert: c => Assert.Equal(Convert(c.Text, c.Factor, c.Target), Convert(c.Text, c.Factor, c.Target)));

    // A line the converter writes nothing of is exactly what the scaler shows, unless it was flagged; a flagged line is as
    // written.
    [Fact]
    public void A_line_not_converted_is_the_scalers_or_flagged_as_written() =>
        Cases.Sample(iter: Iterations, assert: c =>
        {
            var converted = Convert(c.Text, c.Factor, c.Target);
            if (converted.IsConverted)
            {
                return;
            }

            if (converted.Unscaled is not null)
            {
                Assert.Equal(c.Text, converted.Text);
                Assert.False(converted.IsRounded);
                return;
            }

            Assert.Equal(ScalingCorpus.Scale(c.Text, c.Factor), converted);
        });

    // A converted line reads back as what it shows, in the target system (AssertReadsBackInTarget).
    [Fact]
    public void A_converted_line_reads_back_in_the_target_system() =>
        Cases.Sample(iter: Iterations, assert: c => AssertReadsBackInTarget(Convert(c.Text, c.Factor, c.Target), c.Target));

    // "Never two systems mixed by the converter", on lines: a line whose temperatures were in one scale shows them in one
    // scale, as many as before; when they were converted, each is the rounded conversion of the blogger's.
    [Fact]
    public void A_lines_temperatures_convert_together_or_not_at_all() =>
        Cases.Sample(iter: Iterations, assert: c =>
        {
            var converted = Convert(c.Text, c.Factor, c.Target);
            var before = TemperatureMentions.Find(c.Text, 0);
            var after = TemperatureMentions.Find(converted.Text, 0);
            Assert.Equal(before.Count, after.Count);
            if (before.Select(m => m.UnitCode).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                return;
            }

            Assert.True(after.Select(m => m.UnitCode).Distinct(StringComparer.Ordinal).Count() <= 1, $"\"{converted.Text}\" mixes scales.");
            var targetCode = c.Target == UnitSystem.Metric ? "celsius" : "fahrenheit";
            if (before.Count == 0 || before[0].UnitCode == targetCode || after[0].UnitCode != targetCode)
            {
                return;
            }

            for (var i = 0; i < before.Count; i++)
            {
                var low = RecipeConverter.RoundTemperature(RecipeConverter.ConvertTemperature(before[i].Low, before[i].UnitCode), c.Target);
                if (low.Sign > 0)
                {
                    Assert.Equal(low, after[i].Low);
                }
            }
        });

    // Only the quantity, the unit, and temperatures are written anew: before the quantity nothing changes, and a line with
    // no temperature keeps its item, its note, and everything after its unit exactly.
    [Fact]
    public void Only_the_measure_and_temperatures_are_written_anew() =>
        Cases.Sample(iter: Iterations, assert: c =>
        {
            var parse = IngredientLineParser.Parse(c.Text);
            var converted = Convert(c.Text, c.Factor, c.Target);
            if (!converted.IsConverted)
            {
                return;
            }

            var start = parse.QuantitySpan?.Start ?? 0;
            Assert.Equal(c.Text[..start], converted.Text[..start]);
            if (TemperatureMentions.Find(c.Text, 0).Count > 0 || !ReadableAmount(converted))
            {
                return;
            }

            var reread = IngredientLineParser.ParseAnyLength(converted.Text);
            Assert.Equal(parse.Item, reread.Item);
            Assert.Equal(parse.Note, reread.Note);
            Assert.EndsWith(c.Text[parse.UnitSpan!.Value.End..], converted.Text, StringComparison.Ordinal);
        });

    // Each end shown is the exact scaled amount, converted exactly by the units' definitions (and a density, when the item
    // has one), in the unit its exact low end chooses, rounded once - all worked out here on its own.
    [Fact]
    public void Each_end_is_the_exact_conversion_rounded_once() =>
        Cases.Sample(iter: Iterations, assert: c =>
        {
            var parse = IngredientLineParser.Parse(c.Text);
            var converted = Convert(c.Text, c.Factor, c.Target);
            if (!converted.IsConverted || parse.UnitCode is not { } code || !Definitions.TryGetValue(code, out var size))
            {
                return;
            }

            // Only a measure in the other system converts; a spoon stays in metric, and its line's temperatures may still convert.
            var unit = Units.All.Single(u => u.Code == code);
            if (unit.System == c.Target || (c.Target == UnitSystem.Metric && code is "teaspoon" or "tablespoon"))
            {
                return;
            }

            var dimension = unit.Dimension;
            ExactValue across = Rational.One;
            var cup = Definitions["cup"];
            if (IngredientDensities.Find(parse.Item) is { } density)
            {
                if (dimension == UnitDimension.Volume && c.Target == UnitSystem.Metric)
                {
                    (dimension, across) = (UnitDimension.Weight, (ExactValue)density.GramsPerCup / cup);
                }
                else if (dimension == UnitDimension.Weight && c.Target == UnitSystem.UsCustomary)
                {
                    (dimension, across) = (UnitDimension.Volume, (ExactValue)cup / density.GramsPerCup);
                }
            }

            var exact = parse.Quantity!.MultiplyBy(c.Factor.Value);
            var lowBase = exact.Low!.Value * (ExactValue)size * across;
            var target = (c.Target, dimension) switch
            {
                (UnitSystem.Metric, UnitDimension.Volume) => lowBase < (Rational)1000 ? "milliliter" : "liter",
                (UnitSystem.Metric, _) => lowBase < (Rational)1000 ? "gram" : "kilogram",
                (_, UnitDimension.Volume) => lowBase < Definitions["tablespoon"] ? "teaspoon" : lowBase < cup / 4 ? "tablespoon" : "cup",
                _ => lowBase < Definitions["pound"] ? "ounce" : "pound",
            };
            var grid = c.Target == UnitSystem.Metric ? RoundingGrid.Metric : RoundingGrid.Kitchen;
            var low = FriendlyRounding.Round(lowBase / Definitions[target], grid);
            var high = exact.High is { } h ? FriendlyRounding.Round(h * (ExactValue)size * across / Definitions[target], grid) : low;
            Assert.Equal(low == high ? Quantity.Amount(low) : Quantity.Range(low, high), converted.Quantity);
            if (ReadableAmount(converted))
            {
                Assert.Equal(target, IngredientLineParser.ParseAnyLength(converted.Text).UnitCode);
            }
        });

    [Fact]
    public void A_temperature_line_ignores_the_scale() =>
        Cases.Sample(iter: Iterations, assert: c =>
        {
            if (IngredientLineParser.Parse(c.Text).UnitCode is not ("fahrenheit" or "celsius"))
            {
                return;
            }

            Assert.Equal(Convert(c.Text, ScaleFactor.Unchanged, c.Target), Convert(c.Text, c.Factor, c.Target));
        });

    // The largest and smallest factors there are, on every line, in both systems: never an overflow.
    [Fact]
    public void Every_line_converts_at_the_extreme_factors()
    {
        ScaleFactor[] extremes =
        [
            ScaleFactor.Of(RecipeLimits.MaxQuantityTerm),
            ScaleFactor.Of(Rational.Create(1, RecipeLimits.MaxQuantityTerm)),
            ScaleFactor.Of(Rational.Create(RecipeLimits.MaxQuantityTerm - 1, RecipeLimits.MaxQuantityTerm)),
        ];
        foreach (var text in Lines)
        {
            foreach (var factor in extremes)
            {
                foreach (var target in new[] { UnitSystem.Metric, UnitSystem.UsCustomary })
                {
                    var converted = Convert(text, factor, target);
                    Assert.Equal(converted, Convert(text, factor, target));
                    AssertReadsBackInTarget(converted, target);
                    var start = IngredientLineParser.Parse(text).QuantitySpan?.Start ?? 0;
                    Assert.Equal(text[..start], converted.Text[..start]);
                }
            }
        }
    }

    // A converted step keeps every character outside its temperatures, has as many temperatures as before, all in the target
    // scale and each the rounded conversion of the original; converting it again changes nothing.
    [Fact]
    public void A_step_changes_only_its_temperatures_and_only_once() =>
        Gen.Select(Steps, Targets).Sample(iter: Iterations, assert: c =>
        {
            var (text, target) = c;
            var step = RecipeConverter.ConvertStep(text, target);
            Assert.Equal(step, RecipeConverter.ConvertStep(text, target));
            if (!step.IsConverted)
            {
                Assert.Equal(text, step.Text);
                return;
            }

            var before = TemperatureMentions.Find(text, 0);
            var after = TemperatureMentions.Find(step.Text, 0);
            Assert.Equal(before.Count, after.Count);
            var targetCode = target == UnitSystem.Metric ? "celsius" : "fahrenheit";
            var belowZero = new HashSet<int>();
            for (var i = 0; i < before.Count; i++)
            {
                Assert.Equal(targetCode, after[i].UnitCode);
                var low = RecipeConverter.RoundTemperature(RecipeConverter.ConvertTemperature(before[i].Low, before[i].UnitCode), target);
                if (low.Sign < 0)
                {
                    // Written with its minus sign, which the reader finds as a dash before the number.
                    Assert.Equal('-', step.Text[after[i].Numbers.Start - 1]);
                    belowZero.Add(i);
                }
                else
                {
                    Assert.Equal(low, after[i].Low);
                }
            }

            Assert.Equal(Outside(text, before, []), Outside(step.Text, after, belowZero));

            Assert.False(RecipeConverter.ConvertStep(step.Text, target).IsConverted);
        });

    // What the reader sees reads back as exactly what the converter says it shows, and no measure is left in the other system
    // but a teaspoon or tablespoon in metric. A result past the stored term cap is display only, so its amount is not read.
    private static void AssertReadsBackInTarget(ScaledIngredientLine converted, UnitSystem target)
    {
        if (!converted.IsConverted || !ReadableAmount(converted))
        {
            return;
        }

        var reread = IngredientLineParser.ParseAnyLength(converted.Text);
        Assert.Equal(IngredientParseStatus.Parsed, reread.Status);
        Assert.Equal(converted.Quantity, reread.Quantity);
        Assert.Equal(converted.QuantitySpan, reread.QuantitySpan);

        if (reread.UnitCode is { } code && Units.All.Single(u => u.Code == code) is var unit
            && (UnitConversions.Converts(unit) || unit.Dimension == UnitDimension.Temperature))
        {
            Assert.True(
                unit.System == target || (target == UnitSystem.Metric && code is "teaspoon" or "tablespoon"),
                $"\"{converted.Text}\" still measures in {code}.");
        }
    }

    private static ScaledIngredientLine Convert(string text, ScaleFactor factor, UnitSystem target) =>
        ConversionCorpus.Convert(text, factor, target);

    // A shown amount past the stored term cap (1000000 gallons is 3785410 liters) is display only: the parser reads no term
    // past the cap, so such a line is never read back (PP4.7's known gap). Every other amount is.
    private static bool ReadableAmount(ScaledIngredientLine line) => line.Quantity is not { HasAmount: true } quantity || WithinCap(quantity);

    private static bool WithinCap(Quantity quantity) =>
        new[] { quantity.Low, quantity.High }.OfType<Rational>()
            .All(v => v.Numerator <= RecipeLimits.MaxQuantityTerm && v.Denominator <= RecipeLimits.MaxQuantityTerm);

    // The text with each temperature's numbers (and the minus sign of one below zero) and unit taken out, keeping what stands
    // between them.
    private static string Outside(string text, IReadOnlyList<TemperatureMention> mentions, HashSet<int> belowZero)
    {
        var outside = new System.Text.StringBuilder();
        var at = 0;
        for (var i = 0; i < mentions.Count; i++)
        {
            var mention = mentions[i];
            var start = belowZero.Contains(i) ? mention.Numbers.Start - 1 : mention.Numbers.Start;
            outside.Append(text, at, start - at).Append('|')
                .Append(text, mention.Numbers.End, mention.Unit.Start - mention.Numbers.End).Append('|');
            at = mention.Unit.End;
        }

        return outside.Append(text, at, text.Length - at).ToString();
    }
}
